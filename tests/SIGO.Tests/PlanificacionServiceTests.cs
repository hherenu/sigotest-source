using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SIGO.Models;
using SIGO.Models.Enums;
using SIGO.Models.ViewModels;
using SIGO.Services;

namespace SIGO.Tests;

/// <summary>
/// Máquina de estados de Planificación (Pendiente → Cargada → Aprobada con loop
/// EnRevision), bloques, grilla y notificaciones — contra LocalDB real, con un
/// sender de mails falso que registra (o falla a demanda).
/// </summary>
public class PlanificacionServiceTests(LocalDbFixture fx) : IClassFixture<LocalDbFixture>
{
    // ── fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeEmailSender : IEmailSender
    {
        public List<(IReadOnlyCollection<string> Destinatarios, string Asunto)> Enviados { get; } = [];
        public bool Fallar { get; set; }

        public Task<int> EnviarAsync(IReadOnlyCollection<string> destinatarios, string asunto, string cuerpoHtml)
        {
            if (Fallar) throw new InvalidOperationException("SMTP caído (simulado).");
            Enviados.Add((destinatarios, asunto));
            return Task.FromResult(destinatarios.Count);
        }
    }

    private sealed class FakeRecipients : IPlanNotificationRecipients
    {
        public Task<IReadOnlyCollection<string>> ConRolAsync(RolUsuario rol) =>
            Task.FromResult<IReadOnlyCollection<string>>([$"{rol.ToString().ToLower()}@test.local"]);

        public Task<IReadOnlyCollection<string>> DirectorDeObraAsync(int obraId) =>
            Task.FromResult<IReadOnlyCollection<string>>(["director@test.local"]);
    }

    private sealed class OptionsMonitorStub<T>(T valor) : IOptionsMonitor<T>
    {
        public T CurrentValue => valor;
        public T Get(string? name) => valor;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private (PlanificacionService Servicio, FakeEmailSender Mails) Crear(params string[] roles)
    {
        var mails = new FakeEmailSender();
        var servicio = new PlanificacionService(
            new TestDbFactory(fx.Options),
            new FakeCurrentUser(roles),
            mails,
            new FakeRecipients(),
            new OptionsMonitorStub<CorreoOptions>(new CorreoOptions { BaseUrl = "http://test" }),
            NullLogger<PlanificacionService>.Instance);
        return (servicio, mails);
    }

    private async Task<int> CrearObraAsync()
    {
        await using var db = fx.CrearContexto();
        var obra = new Obra { Nombre = $"Obra plan {Guid.NewGuid():N}", NumeroLicitacion = "LP PLAN" };
        db.Obras.Add(obra);
        await db.SaveChangesAsync();
        return obra.Id;
    }

    private static readonly List<PeriodoVM> PeriodosTest = [new(2030, 1), new(2030, 2)];

    // ── tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetOrCreate_CreaPendienteYEsIdempotente()
    {
        var obraId = await CrearObraAsync();
        var (servicio, _) = Crear(Roles.Admin);

        var plan1 = await servicio.GetOrCreateAsync(obraId);
        var plan2 = await servicio.GetOrCreateAsync(obraId);

        Assert.Equal(EstadoPlanificacion.Pendiente, plan1.Estado);
        Assert.Equal(plan1.Id, plan2.Id); // un solo plan por obra
    }

    [Fact]
    public async Task AgregarAutorizante_BasicaUnicaYAdicionalesAutonumeran()
    {
        var obraId = await CrearObraAsync();
        var (servicio, _) = Crear(Roles.Admin);
        var plan = await servicio.GetOrCreateAsync(obraId);

        await servicio.AgregarAutorizanteAsync(plan.Id, TipoAutorizante.Basica, null, null);
        var ad1 = await servicio.AgregarAutorizanteAsync(plan.Id, TipoAutorizante.Adicional, null, "Refuerzo");
        var ad2 = await servicio.AgregarAutorizanteAsync(plan.Id, TipoAutorizante.Adicional, null, null);

        Assert.Equal(1, ad1.Numero);
        Assert.Equal(2, ad2.Numero);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            servicio.AgregarAutorizanteAsync(plan.Id, TipoAutorizante.Basica, null, null));
        Assert.Contains("ya existe", ex.Message);
    }

    [Fact]
    public async Task MarcarCargada_SinBloques_Rechaza()
    {
        var obraId = await CrearObraAsync();
        var (servicio, _) = Crear(Roles.Admin);
        var plan = await servicio.GetOrCreateAsync(obraId);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.MarcarCargadaAsync(plan.Id));
        Assert.Contains("no tiene bloques", ex.Message);
    }

    [Fact]
    public async Task BuildVm_BucketAnualHuerfano_VuelveEditableYSePuedeVaciar()
    {
        // Obra 01/2030..12/2030: la grilla mensualiza 2030 y agrega buckets 2031/2032.
        int obraId;
        await using (var db = fx.CrearContexto())
        {
            var obra = new Obra
            {
                Nombre = $"Obra plan {Guid.NewGuid():N}", NumeroLicitacion = "LP PLAN",
                FechaActaInicio = new DateTime(2030, 1, 1), FechaFinalContrato = new DateTime(2030, 12, 1)
            };
            db.Obras.Add(obra);
            await db.SaveChangesAsync();
            obraId = obra.Id;
        }
        var (servicio, _) = Crear(Roles.Admin);
        var plan = await servicio.GetOrCreateAsync(obraId);
        var basica = await servicio.AgregarAutorizanteAsync(plan.Id, TipoAutorizante.Basica, null, null);

        var vm0 = await servicio.BuildVmAsync(obraId);
        await servicio.GuardarGrillaAsync(plan.Id,
        [
            new(basica.Id, ConceptoPlanMonto.MontoAutorizado, null, null, Moneda.Pesos, 100m),
            new(basica.Id, ConceptoPlanMonto.CalculoAnual, 2031, null, Moneda.Pesos, 100m),
        ], vm0.Periodos, vm0.RowVersion);

        // Ampliación de plazo: 2031 pasa a renderizarse mensual (hasta jun/2031) y el
        // CalculoAnual 2031 guardado queda sin bucket propio en la cola normal.
        await using (var db = fx.CrearContexto())
        {
            (await db.Obras.SingleAsync(o => o.Id == obraId)).FechaFinalContrato = new DateTime(2031, 6, 1);
            await db.SaveChangesAsync();
        }

        var vm = await servicio.BuildVmAsync(obraId);

        // El bucket huérfano se renderiza editable junto a los meses de 2031, con la
        // cola anual ordenada por año, y deja de figurar como "fuera de horizonte".
        Assert.Contains(vm.Periodos, p => p.EsAnual && p.Anio == 2031);
        Assert.Contains(vm.Periodos, p => !p.EsAnual && p.Anio == 2031 && p.Mes == 6);
        var anuales = vm.Periodos.Where(p => p.EsAnual).Select(p => p.Anio).ToList();
        Assert.Equal(anuales.OrderBy(a => a).ToList(), anuales);
        var render = vm.Periodos
            .Select(p => (p.EsAnual ? ConceptoPlanMonto.CalculoAnual : ConceptoPlanMonto.Mensual, (int?)p.Anio, p.Mes))
            .ToHashSet();
        Assert.Empty(vm.Bloques.Single().DetalleFueraDeHorizonte(render));

        // Redistribuir: el monto pasa a jun/2031 y el bucket (no enviado) se elimina.
        await servicio.GuardarGrillaAsync(plan.Id,
        [
            new(basica.Id, ConceptoPlanMonto.MontoAutorizado, null, null, Moneda.Pesos, 100m),
            new(basica.Id, ConceptoPlanMonto.Mensual, 2031, 6, Moneda.Pesos, 100m),
        ], vm.Periodos, vm.RowVersion);

        var final = await servicio.BuildVmAsync(obraId);
        Assert.DoesNotContain(final.Periodos, p => p.EsAnual && p.Anio == 2031);
        Assert.Equal(100m, final.Bloques.Single().Valor(ConceptoPlanMonto.Mensual, 2031, 6, Moneda.Pesos));
    }

    [Fact]
    public async Task EnviarARevision_ConMotivoSoloEspacios_Rechaza()
    {
        var obraId = await CrearObraAsync();
        var (servicio, _) = Crear(Roles.Admin);
        var plan = await servicio.GetOrCreateAsync(obraId);
        await servicio.AgregarAutorizanteAsync(plan.Id, TipoAutorizante.Basica, null, null);
        await servicio.MarcarCargadaAsync(plan.Id);

        // El RequiredValidator del form deja pasar espacios; sin el espejo server el
        // plan quedaba EnRevision con la alerta oculta y el mail al director en blanco.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            servicio.EnviarARevisionAsync(plan.Id, "   ", null));
        Assert.Contains("motivo", ex.Message);

        await using var db = fx.CrearContexto();
        Assert.Equal(EstadoPlanificacion.Cargada, (await db.Planificaciones.SingleAsync(p => p.Id == plan.Id)).Estado);
    }

    [Fact]
    public async Task CircuitoCompleto_CargadaRevisionCargadaAprobada_ConMails()
    {
        var obraId = await CrearObraAsync();
        var (servicio, mails) = Crear(Roles.Admin);
        var plan = await servicio.GetOrCreateAsync(obraId);
        await servicio.AgregarAutorizanteAsync(plan.Id, TipoAutorizante.Basica, null, null);

        await servicio.MarcarCargadaAsync(plan.Id);                       // → Gerente
        await servicio.EnviarARevisionAsync(plan.Id, "Faltan meses", null); // → Director
        await servicio.MarcarCargadaAsync(plan.Id);                       // → Gerente
        await servicio.AprobarAsync(plan.Id);                             // → Presupuesto

        await using var db = fx.CrearContexto();
        var final = await db.Planificaciones.SingleAsync(p => p.Id == plan.Id);
        Assert.Equal(EstadoPlanificacion.Aprobada, final.Estado);
        Assert.NotNull(final.FechaAprobacion);
        Assert.Null(final.MotivoRevision); // la re-carga limpió el motivo

        Assert.Equal(4, mails.Enviados.Count);
        Assert.Contains("director@test.local", mails.Enviados[1].Destinatarios);
        Assert.Contains("presupuesto@test.local", mails.Enviados[3].Destinatarios);
    }

    [Fact]
    public async Task Aprobar_DesdePendiente_Rechaza()
    {
        var obraId = await CrearObraAsync();
        var (servicio, _) = Crear(Roles.Admin);
        var plan = await servicio.GetOrCreateAsync(obraId);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.AprobarAsync(plan.Id));
        Assert.Contains("Cargado", ex.Message);
    }

    [Fact]
    public async Task MailCaido_NoRevierteLaTransicion()
    {
        var obraId = await CrearObraAsync();
        var (servicio, mails) = Crear(Roles.Admin);
        var plan = await servicio.GetOrCreateAsync(obraId);
        await servicio.AgregarAutorizanteAsync(plan.Id, TipoAutorizante.Basica, null, null);

        mails.Fallar = true;
        await servicio.MarcarCargadaAsync(plan.Id); // no debe lanzar: mail best-effort post-commit

        await using var db = fx.CrearContexto();
        Assert.Equal(EstadoPlanificacion.Cargada,
            (await db.Planificaciones.SingleAsync(p => p.Id == plan.Id)).Estado);
    }

    [Fact]
    public async Task EliminarUltimoBloque_VuelveAPendienteYLimpiaElRastro()
    {
        var obraId = await CrearObraAsync();
        var (servicio, _) = Crear(Roles.Admin);
        var plan = await servicio.GetOrCreateAsync(obraId);
        var basica = await servicio.AgregarAutorizanteAsync(plan.Id, TipoAutorizante.Basica, null, null);
        await servicio.MarcarCargadaAsync(plan.Id);

        await servicio.EliminarAutorizanteAsync(basica.Id);

        await using var db = fx.CrearContexto();
        var final = await db.Planificaciones.Include(p => p.Autorizantes).SingleAsync(p => p.Id == plan.Id);
        Assert.Empty(final.Autorizantes);
        Assert.Equal(EstadoPlanificacion.Pendiente, final.Estado);
        Assert.Null(final.FechaCarga);
    }

    [Fact]
    public async Task GuardarGrilla_PreservaMontosFueraDelHorizonteEditado()
    {
        var obraId = await CrearObraAsync();
        var (servicio, _) = Crear(Roles.Admin);
        var plan = await servicio.GetOrCreateAsync(obraId);
        var basica = await servicio.AgregarAutorizanteAsync(plan.Id, TipoAutorizante.Basica, null, null);

        // Mes dentro y mes fuera del horizonte que la grilla va a editar. El autorizado
        // acompaña la curva: la regla Autorizado vs Planificado = 0 exige el balance.
        await servicio.GuardarGrillaAsync(plan.Id,
            [new PlanMontoInput(basica.Id, ConceptoPlanMonto.MontoAutorizado, null, null, Moneda.Pesos, 1099m),
             new PlanMontoInput(basica.Id, ConceptoPlanMonto.Mensual, 2030, 1, Moneda.Pesos, 100m),
             new PlanMontoInput(basica.Id, ConceptoPlanMonto.Mensual, 2035, 6, Moneda.Pesos, 999m)],
            [new PeriodoVM(2030, 1), new PeriodoVM(2035, 6)]);

        // Nuevo guardado que solo edita 2030: borra y reescribe ese mes, pero el
        // monto de 2035 (fuera del horizonte renderizado) debe sobrevivir — y el
        // balance se exige contra el estado final INCLUYENDO esa fila preservada.
        await servicio.GuardarGrillaAsync(plan.Id,
            [new PlanMontoInput(basica.Id, ConceptoPlanMonto.MontoAutorizado, null, null, Moneda.Pesos, 1199m),
             new PlanMontoInput(basica.Id, ConceptoPlanMonto.Mensual, 2030, 1, Moneda.Pesos, 200m)],
            PeriodosTest);

        await using var db = fx.CrearContexto();
        var montos = await db.PlanMontos.Where(m => m.AutorizanteId == basica.Id)
            .OrderBy(m => m.Anio).ToListAsync();
        Assert.Equal(3, montos.Count);
        Assert.Equal(1199m, montos[0].Monto); // autorizado (sin período, ordena primero)
        Assert.Equal(200m, montos[1].Monto);  // reescrito
        Assert.Equal(999m, montos[2].Monto);  // preservado
    }

    [Fact]
    public async Task GuardarGrilla_SinBloques_Rechaza()
    {
        var obraId = await CrearObraAsync();
        var (servicio, _) = Crear(Roles.Admin);
        var plan = await servicio.GetOrCreateAsync(obraId);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            servicio.GuardarGrillaAsync(plan.Id, [], PeriodosTest));
        Assert.Contains("no tiene bloques", ex.Message);
    }

    [Fact]
    public async Task GuardarGrilla_Desbalanceada_RechazaSinEscribirNada()
    {
        var obraId = await CrearObraAsync();
        var (servicio, _) = Crear(Roles.Admin);
        var plan = await servicio.GetOrCreateAsync(obraId);
        var basica = await servicio.AgregarAutorizanteAsync(plan.Id, TipoAutorizante.Basica, null, null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.GuardarGrillaAsync(plan.Id,
            [new PlanMontoInput(basica.Id, ConceptoPlanMonto.MontoAutorizado, null, null, Moneda.Pesos, 1000m),
             new PlanMontoInput(basica.Id, ConceptoPlanMonto.Mensual, 2030, 1, Moneda.Pesos, 300m)],
            PeriodosTest));
        Assert.Contains("Autorizado vs Planificado", ex.Message);

        // El rechazo es previo a cualquier escritura: no quedó nada guardado.
        await using var db = fx.CrearContexto();
        Assert.Equal(0, await db.PlanMontos.CountAsync(m => m.AutorizanteId == basica.Id));
    }

    [Fact]
    public async Task MarcarCargada_Desbalanceada_Rechaza()
    {
        var obraId = await CrearObraAsync();
        var (servicio, _) = Crear(Roles.Admin);
        var plan = await servicio.GetOrCreateAsync(obraId);
        var basica = await servicio.AgregarAutorizanteAsync(plan.Id, TipoAutorizante.Basica, null, null);

        // Desbalance inyectado por fuera del guardado (datos previos a la regla):
        // la transición es el backstop.
        await using (var db = fx.CrearContexto())
        {
            db.PlanMontos.Add(new PlanMonto
            {
                AutorizanteId = basica.Id, Concepto = ConceptoPlanMonto.MontoAutorizado,
                Moneda = Moneda.Pesos, Monto = 500m
            });
            await db.SaveChangesAsync();
        }

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.MarcarCargadaAsync(plan.Id));
        Assert.Contains("Autorizado vs Planificado", ex.Message);
    }

    [Fact]
    public async Task Aprobar_Desbalanceado_Rechaza()
    {
        var obraId = await CrearObraAsync();
        var (servicio, _) = Crear(Roles.Admin);
        var plan = await servicio.GetOrCreateAsync(obraId);
        var basica = await servicio.AgregarAutorizanteAsync(plan.Id, TipoAutorizante.Basica, null, null);
        await servicio.GuardarGrillaAsync(plan.Id,
            [new PlanMontoInput(basica.Id, ConceptoPlanMonto.MontoAutorizado, null, null, Moneda.Pesos, 100m),
             new PlanMontoInput(basica.Id, ConceptoPlanMonto.Mensual, 2030, 1, Moneda.Pesos, 100m)],
            PeriodosTest);
        await servicio.MarcarCargadaAsync(plan.Id);

        // Se desbalancea después de Cargada (por fuera del guardado): Aprobar lo frena.
        await using (var db = fx.CrearContexto())
        {
            db.PlanMontos.Add(new PlanMonto
            {
                AutorizanteId = basica.Id, Concepto = ConceptoPlanMonto.Mensual,
                Anio = 2030, Mes = 2, Moneda = Moneda.Pesos, Monto = 50m
            });
            await db.SaveChangesAsync();
        }

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.AprobarAsync(plan.Id));
        Assert.Contains("Autorizado vs Planificado", ex.Message);
    }

    [Fact]
    public async Task GuardarGrilla_ConTokenDeSesionViejo_DaConflicto()
    {
        var obraId = await CrearObraAsync();
        var (servicio, _) = Crear(Roles.Admin);
        var plan = await servicio.GetOrCreateAsync(obraId);
        var basica = await servicio.AgregarAutorizanteAsync(plan.Id, TipoAutorizante.Basica, null, null);
        var tokenViejo = plan.RowVersion;

        // Otro guardado en el medio bumpea el rowversion del plan.
        await servicio.GuardarGrillaAsync(plan.Id,
            [new PlanMontoInput(basica.Id, ConceptoPlanMonto.MontoAutorizado, null, null, Moneda.Pesos, 100m),
             new PlanMontoInput(basica.Id, ConceptoPlanMonto.Mensual, 2030, 1, Moneda.Pesos, 100m)],
            PeriodosTest);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            servicio.GuardarGrillaAsync(plan.Id,
                [new PlanMontoInput(basica.Id, ConceptoPlanMonto.MontoAutorizado, null, null, Moneda.Pesos, 50m),
                 new PlanMontoInput(basica.Id, ConceptoPlanMonto.Mensual, 2030, 1, Moneda.Pesos, 50m)],
                PeriodosTest, tokenViejo));
    }

    [Fact]
    public async Task DirectorPuro_SoloOperaSusObrasAsignadas()
    {
        var obraId = await CrearObraAsync();
        var (admin, _) = Crear(Roles.Admin);
        var plan = await admin.GetOrCreateAsync(obraId);
        await admin.AgregarAutorizanteAsync(plan.Id, TipoAutorizante.Basica, null, null);

        // Director sin la obra asignada: rechazo aunque tenga el rol de carga.
        var (director, _) = Crear(Roles.Director);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => director.MarcarCargadaAsync(plan.Id));
        Assert.Contains("no está asignada", ex.Message);

        // Se le asigna la obra (mismo WindowsUser que FakeCurrentUser) → puede.
        await using (var db = fx.CrearContexto())
        {
            var usuario = new Usuario { WindowsUser = @"TEST\tester", Nombre = "Tester", Activo = true };
            db.Usuarios.Add(usuario);
            await db.SaveChangesAsync();
            var obra = await db.Obras.SingleAsync(o => o.Id == obraId);
            obra.DirectorUsuarioId = usuario.Id;
            await db.SaveChangesAsync();
        }
        await director.MarcarCargadaAsync(plan.Id);

        await using var db2 = fx.CrearContexto();
        Assert.Equal(EstadoPlanificacion.Cargada,
            (await db2.Planificaciones.SingleAsync(p => p.Id == plan.Id)).Estado);
    }

    [Fact]
    public async Task RolSinPermisoDeEdicion_Rechaza()
    {
        var obraId = await CrearObraAsync();
        var (admin, _) = Crear(Roles.Admin);
        var plan = await admin.GetOrCreateAsync(obraId);

        var (presupuesto, _) = Crear(Roles.Presupuesto); // solo lectura en el circuito
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            presupuesto.AgregarAutorizanteAsync(plan.Id, TipoAutorizante.Basica, null, null));
        Assert.Contains("permiso", ex.Message);
    }
}
