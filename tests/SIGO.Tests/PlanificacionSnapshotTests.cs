using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SIGO.Models;
using SIGO.Models.Enums;
using SIGO.Models.ViewModels;
using SIGO.Services;

namespace SIGO.Tests;

/// <summary>
/// Ciclo mensual de la planificación: toma de conocimiento de Presupuesto (genera el
/// snapshot del mes; sin toma no hay foto), reemplazo dentro del mismo mes tras un
/// loop de revisión, y el rollover que reinicia el ciclo salvo obras finalizadas.
/// </summary>
public class PlanificacionSnapshotTests(LocalDbFixture fx) : IClassFixture<LocalDbFixture>
{
    private sealed class SilentEmailSender : IEmailSender
    {
        public Task<int> EnviarAsync(IReadOnlyCollection<string> destinatarios, string asunto, string cuerpoHtml)
            => Task.FromResult(destinatarios.Count);
    }

    private sealed class FakeRecipients : IPlanNotificationRecipients
    {
        public Task<IReadOnlyCollection<string>> ConRolAsync(RolUsuario rol) =>
            Task.FromResult<IReadOnlyCollection<string>>([]);
        public Task<IReadOnlyCollection<string>> DirectorDeObraAsync(int obraId) =>
            Task.FromResult<IReadOnlyCollection<string>>([]);
    }

    private sealed class OptionsMonitorStub<T>(T valor) : IOptionsMonitor<T>
    {
        public T CurrentValue => valor;
        public T Get(string? name) => valor;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private PlanificacionService Crear(params string[] roles) => new(
        new TestDbFactory(fx.Options),
        new FakeCurrentUser(roles),
        new SilentEmailSender(),
        new FakeRecipients(),
        new OptionsMonitorStub<CorreoOptions>(new CorreoOptions()),
        NullLogger<PlanificacionService>.Instance);

    private static readonly List<PeriodoVM> PeriodosTest = [new(2030, 1), new(2030, 2)];

    /// <summary>Obra + plan con un bloque y montos, llevado hasta Aprobada.</summary>
    private async Task<(int ObraId, int PlanId, int AutorizanteId)> CrearPlanAprobadoAsync(decimal montoMensual = 100m)
    {
        await using var db = fx.CrearContexto();
        // Presupuesto oficial = curva: el circuito exige el balance de la básica contra
        // el presupuesto de la obra.
        var obra = new Obra
        {
            Nombre = $"Obra snap {Guid.NewGuid():N}", NumeroLicitacion = "LP SNAP",
            PresupuestoOficial = montoMensual, FechaFinalContrato = new DateTime(2030, 12, 1)
        };
        db.Obras.Add(obra);
        await db.SaveChangesAsync();

        var admin = Crear(Roles.Admin);
        var plan = await admin.GetOrCreateAsync(obra.Id);
        var basica = await admin.AgregarAutorizanteAsync(plan.Id, TipoAutorizante.Basica, null, null);
        await admin.GuardarGrillaAsync(plan.Id,
            [new PlanMontoInput(basica.Id, ConceptoPlanMonto.Mensual, 2030, 1, Moneda.Pesos, montoMensual)],
            PeriodosTest);
        await admin.MarcarCargadaAsync(plan.Id);
        await admin.AprobarAsync(plan.Id);
        return (obra.Id, plan.Id, basica.Id);
    }

    // ── Toma de conocimiento ─────────────────────────────────────────────────

    [Fact]
    public async Task TomarConocimiento_GeneraSnapshotDelMesYEstampaElFlag()
    {
        var (obraId, planId, _) = await CrearPlanAprobadoAsync();

        // Presupuesto tiene exactamente esta única escritura.
        await Crear(Roles.Presupuesto).TomarConocimientoAsync(planId);

        await using var db = fx.CrearContexto();
        var plan = await db.Planificaciones.SingleAsync(p => p.Id == planId);
        Assert.NotNull(plan.FechaTomaConocimiento);
        Assert.Equal(@"TEST\tester", plan.TomadaConocimientoPor);

        var hoy = DateTime.Now;
        var snap = await db.PlanificacionSnapshots.Include(s => s.Montos)
            .SingleAsync(s => s.ObraId == obraId);
        Assert.Equal((hoy.Year, hoy.Month), (snap.Anio, snap.Mes));
        Assert.Equal(2, snap.Montos.Count); // autorizado (presupuesto de la obra) + mensual, denormalizados
        Assert.Contains(snap.Montos, m =>
            m.Tipo == TipoAutorizante.Basica && m.Concepto == ConceptoPlanMonto.Mensual && m.Monto == 100m);
        Assert.Contains(snap.Montos, m =>
            m.Tipo == TipoAutorizante.Basica && m.Concepto == ConceptoPlanMonto.MontoAutorizado
            && m.Moneda == Moneda.Pesos && m.Monto == 100m);
    }

    [Fact]
    public async Task TomarConocimiento_SinAprobadaODoble_Rechaza()
    {
        var (_, planId, _) = await CrearPlanAprobadoAsync();
        var presupuesto = Crear(Roles.Presupuesto);

        await presupuesto.TomarConocimientoAsync(planId);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            presupuesto.TomarConocimientoAsync(planId));
        Assert.Contains("ya fue registrada", ex.Message);

        // Y con el plan fuera de Aprobada tampoco.
        await Crear(Roles.Admin).EnviarARevisionAsync(planId, "cambio", null);
        var ex2 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            presupuesto.TomarConocimientoAsync(planId));
        Assert.Contains("Aprobado", ex2.Message);
    }

    [Fact]
    public async Task EnviarARevision_ReseteaLaTomaDeConocimiento()
    {
        var (_, planId, _) = await CrearPlanAprobadoAsync();
        await Crear(Roles.Presupuesto).TomarConocimientoAsync(planId);

        await Crear(Roles.Admin).EnviarARevisionAsync(planId, "corregir curva", null);

        await using var db = fx.CrearContexto();
        var plan = await db.Planificaciones.SingleAsync(p => p.Id == planId);
        Assert.Null(plan.FechaTomaConocimiento);
        Assert.Null(plan.TomadaConocimientoPor);
    }

    [Fact]
    public async Task NuevaTomaEnElMismoMes_ReemplazaElSnapshot()
    {
        var (obraId, planId, autId) = await CrearPlanAprobadoAsync(montoMensual: 100m);
        var presupuesto = Crear(Roles.Presupuesto);
        var admin = Crear(Roles.Admin);

        await presupuesto.TomarConocimientoAsync(planId);

        // Loop de revisión dentro del mismo mes: se adjudica por 250, se corrige la curva
        // y se re-aprueba (la básica balancea contra el adjudicado).
        await admin.EnviarARevisionAsync(planId, "monto mal", null);
        await using (var dbObra = fx.CrearContexto())
        {
            (await dbObra.Obras.SingleAsync(o => o.Id == obraId)).PresupuestoAdjudicado = 250m;
            await dbObra.SaveChangesAsync();
        }
        await admin.GuardarGrillaAsync(planId,
            [new PlanMontoInput(autId, ConceptoPlanMonto.Mensual, 2030, 1, Moneda.Pesos, 250m)],
            PeriodosTest);
        await admin.MarcarCargadaAsync(planId);
        await admin.AprobarAsync(planId);
        await presupuesto.TomarConocimientoAsync(planId);

        // Vale la última versión conocida del mes: un solo snapshot, con el monto nuevo.
        await using var db = fx.CrearContexto();
        var snap = await db.PlanificacionSnapshots.Include(s => s.Montos)
            .SingleAsync(s => s.ObraId == obraId);
        Assert.Contains(snap.Montos, m => m.Concepto == ConceptoPlanMonto.Mensual && m.Monto == 250m);
        Assert.DoesNotContain(snap.Montos, m => m.Monto == 100m);
    }

    [Fact]
    public async Task RolSinConocimiento_Rechaza()
    {
        var (_, planId, _) = await CrearPlanAprobadoAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Crear(Roles.Gerente).TomarConocimientoAsync(planId));
        Assert.Contains("permiso", ex.Message);
    }

    // ── Rollover mensual ─────────────────────────────────────────────────────

    [Fact]
    public async Task Rollover_ReseteaLasTomasSalvoObraFinalizada_YEsIdempotente()
    {
        var (obraActivaId, planActivoId, _) = await CrearPlanAprobadoAsync();
        var (obraFinalId, planFinalizadoId, _) = await CrearPlanAprobadoAsync();
        var presupuesto = Crear(Roles.Presupuesto);
        await presupuesto.TomarConocimientoAsync(planActivoId);
        await presupuesto.TomarConocimientoAsync(planFinalizadoId);
        await Crear(Roles.Admin).MarcarObraFinalizadaAsync(planFinalizadoId, true);

        // Un mes ficticio para no chocar con otros tests que corran el mes real.
        var diaRollover = new DateTime(2091, 5, 9);

        int? reiniciados;
        await using (var db = fx.CrearContexto())
            reiniciados = await PlanificacionRolloverService.EjecutarAsync(db, diaRollover);

        // Al menos el plan activo (la fixture es compartida: otros tests pueden
        // haber dejado más planes con toma registrada).
        Assert.True(reiniciados >= 1);

        await using (var db = fx.CrearContexto())
        {
            var activo = await db.Planificaciones.SingleAsync(p => p.Id == planActivoId);
            var finalizado = await db.Planificaciones.SingleAsync(p => p.Id == planFinalizadoId);
            Assert.Null(activo.FechaTomaConocimiento);        // ciclo nuevo
            Assert.NotNull(finalizado.FechaTomaConocimiento); // fuera del ciclo

            // La historia no se toca: los snapshots de ambos siguen.
            Assert.Equal(1, await db.PlanificacionSnapshots.CountAsync(s => s.ObraId == obraActivaId));
            Assert.Equal(1, await db.PlanificacionSnapshots.CountAsync(s => s.ObraId == obraFinalId));
        }

        // Segunda corrida del mismo mes: idempotente.
        await using (var db = fx.CrearContexto())
            Assert.Null(await PlanificacionRolloverService.EjecutarAsync(db, diaRollover));
    }
}
