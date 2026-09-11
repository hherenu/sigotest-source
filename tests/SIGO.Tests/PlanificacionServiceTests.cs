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
        public List<(IReadOnlyCollection<string> Destinatarios, string Asunto, string Cuerpo)> Enviados { get; } = [];
        public bool Fallar { get; set; }

        public Task<int> EnviarAsync(IReadOnlyCollection<string> destinatarios, string asunto, string cuerpoHtml)
        {
            if (Fallar) throw new InvalidOperationException("SMTP caído (simulado).");
            Enviados.Add((destinatarios, asunto, cuerpoHtml));
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

    /// <summary>
    /// Obra mínima con plazo (participa de Planificación). Sin presupuesto (0) el circuito
    /// no puede avanzar: los tests que lo recorren lo cargan. <paramref name="proyectada"/>
    /// la deja sin plazo ni antecedentes (fuera del módulo).
    /// </summary>
    private async Task<int> CrearObraAsync(decimal presupuestoOficial = 0m, bool proyectada = false)
    {
        await using var db = fx.CrearContexto();
        var obra = new Obra
        {
            Nombre = $"Obra plan {Guid.NewGuid():N}", NumeroLicitacion = "LP PLAN",
            PresupuestoOficial = presupuestoOficial,
            FechaFinalContrato = proyectada ? null : new DateTime(2030, 12, 1)
        };
        db.Obras.Add(obra);
        await db.SaveChangesAsync();
        return obra.Id;
    }

    private static readonly List<PeriodoVM> PeriodosTest = [new(2030, 1), new(2030, 2)];

    /// <summary>
    /// Obra con presupuesto oficial + plan con Obra Básica balanceada (curva = presupuesto):
    /// el punto de partida para el circuito, que exige presupuesto cargado y balance.
    /// </summary>
    private async Task<(int ObraId, Planificacion Plan, Autorizante Basica)> CrearPlanBalanceadoAsync(
        PlanificacionService servicio, decimal presupuesto = 100m)
    {
        var obraId = await CrearObraAsync(presupuesto);
        var plan = await servicio.GetOrCreateAsync(obraId);
        var basica = await servicio.AgregarAutorizanteAsync(plan.Id, TipoAutorizante.Basica, null, null);
        await servicio.GuardarGrillaAsync(plan.Id,
            [new PlanMontoInput(basica.Id, ConceptoPlanMonto.Mensual, 2030, 1, Moneda.Pesos, presupuesto)],
            PeriodosTest);
        return (obraId, plan, basica);
    }

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
        var (servicio, _) = Crear(Roles.Admin);
        var (_, plan, _) = await CrearPlanBalanceadoAsync(servicio);
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
        var (servicio, mails) = Crear(Roles.Admin);
        var (_, plan, _) = await CrearPlanBalanceadoAsync(servicio);

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

        // El mail de aprobación pide la acción que le toca a Presupuesto: tomar
        // conocimiento (es lo que registra la versión del mes), no es informativo.
        Assert.Contains("Tomá conocimiento", mails.Enviados[3].Cuerpo);
        Assert.DoesNotContain("no requiere ninguna acción", mails.Enviados[3].Cuerpo);
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
        var (servicio, mails) = Crear(Roles.Admin);
        var (_, plan, _) = await CrearPlanBalanceadoAsync(servicio);

        mails.Fallar = true;
        await servicio.MarcarCargadaAsync(plan.Id); // no debe lanzar: mail best-effort post-commit

        await using var db = fx.CrearContexto();
        Assert.Equal(EstadoPlanificacion.Cargada,
            (await db.Planificaciones.SingleAsync(p => p.Id == plan.Id)).Estado);
    }

    [Fact]
    public async Task EliminarUltimoBloque_VuelveAPendienteYLimpiaElRastro()
    {
        var (servicio, _) = Crear(Roles.Admin);
        var (_, plan, basica) = await CrearPlanBalanceadoAsync(servicio);
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

        // Mes dentro y mes fuera del horizonte que la grilla va a editar.
        await servicio.GuardarGrillaAsync(plan.Id,
            [new PlanMontoInput(basica.Id, ConceptoPlanMonto.Mensual, 2030, 1, Moneda.Pesos, 100m),
             new PlanMontoInput(basica.Id, ConceptoPlanMonto.Mensual, 2035, 6, Moneda.Pesos, 999m)],
            [new PeriodoVM(2030, 1), new PeriodoVM(2035, 6)]);

        // Nuevo guardado que solo edita 2030: borra y reescribe ese mes, pero el
        // monto de 2035 (fuera del horizonte renderizado) debe sobrevivir.
        await servicio.GuardarGrillaAsync(plan.Id,
            [new PlanMontoInput(basica.Id, ConceptoPlanMonto.Mensual, 2030, 1, Moneda.Pesos, 200m)],
            PeriodosTest);

        await using var db = fx.CrearContexto();
        var montos = await db.PlanMontos.Where(m => m.AutorizanteId == basica.Id)
            .OrderBy(m => m.Anio).ToListAsync();
        Assert.Equal(2, montos.Count);
        Assert.Equal(200m, montos[0].Monto);  // reescrito
        Assert.Equal(999m, montos[1].Monto);  // preservado
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
    public async Task GuardarGrilla_Desbalanceada_SeGuardaPeroNoPasaACargada()
    {
        var obraId = await CrearObraAsync(presupuestoOficial: 1000m);
        var (servicio, _) = Crear(Roles.Admin);
        var plan = await servicio.GetOrCreateAsync(obraId);
        var basica = await servicio.AgregarAutorizanteAsync(plan.Id, TipoAutorizante.Basica, null, null);

        // El guardado no exige el balance: queda registrado tal cual y devuelve el aviso
        // (la misma regla que después exige el cambio de paso).
        var avisos = await servicio.GuardarGrillaAsync(plan.Id,
            [new PlanMontoInput(basica.Id, ConceptoPlanMonto.Mensual, 2030, 1, Moneda.Pesos, 300m)],
            PeriodosTest);
        Assert.Single(avisos, a => a.Contains("Autorizado vs Planificado") && a.Contains("falta planificar 700"));
        await using (var db = fx.CrearContexto())
            Assert.Equal(1, await db.PlanMontos.CountAsync(m => m.AutorizanteId == basica.Id));

        // El cambio de paso es el que lo exige: sigue Pendiente.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.MarcarCargadaAsync(plan.Id));
        Assert.Contains("Autorizado vs Planificado", ex.Message);
        Assert.Contains("falta planificar 700", ex.Message);
        Assert.Equal(EstadoPlanificacion.Pendiente, (await servicio.BuildVmAsync(obraId)).Estado);
    }

    [Fact]
    public async Task Aprobar_Desbalanceado_Rechaza()
    {
        var (servicio, _) = Crear(Roles.Admin);
        var (_, plan, basica) = await CrearPlanBalanceadoAsync(servicio);
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
        var (admin, _) = Crear(Roles.Admin);
        var (obraId, plan, _) = await CrearPlanBalanceadoAsync(admin);

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

        // El gerente aprueba o envía a revisión, pero no edita la grilla.
        var basica = await admin.AgregarAutorizanteAsync(plan.Id, TipoAutorizante.Basica, null, null);
        var (gerente, _) = Crear(Roles.Gerente);
        var exGrilla = await Assert.ThrowsAsync<InvalidOperationException>(() => gerente.GuardarGrillaAsync(plan.Id,
            [new PlanMontoInput(basica.Id, ConceptoPlanMonto.MontoAutorizado, null, null, Moneda.Pesos, 100m)],
            PeriodosTest));
        Assert.Contains("permiso", exGrilla.Message);
        var exBloque = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gerente.AgregarAutorizanteAsync(plan.Id, TipoAutorizante.Adicional, null, "Extra"));
        Assert.Contains("permiso", exBloque.Message);
    }

    // ── Mes del anticipo ≤ primer mes del plan (inicio de la obra) ────────────

    [Fact]
    public async Task AnticipoPosteriorAlInicio_SeGuardaPeroNoPasaACargada()
    {
        int obraId;
        await using (var db = fx.CrearContexto())
        {
            var obra = new Obra
            {
                Nombre = $"Obra plan {Guid.NewGuid():N}", NumeroLicitacion = "LP PLAN",
                FechaActaInicio = new DateTime(2030, 3, 15), FechaFinalContrato = new DateTime(2030, 12, 1),
                PresupuestoOficial = 1000m
            };
            db.Obras.Add(obra);
            await db.SaveChangesAsync();
            obraId = obra.Id;
        }
        var (servicio, _) = Crear(Roles.Admin);
        var plan = await servicio.GetOrCreateAsync(obraId);
        var basica = await servicio.AgregarAutorizanteAsync(plan.Id, TipoAutorizante.Basica, null, null);
        var vm = await servicio.BuildVmAsync(obraId);

        // Anticipo en abr/2030, un mes después del acta (mar/2030): la grilla lo guarda
        // igual (la página solo avisa)...
        await servicio.GuardarGrillaAsync(plan.Id,
            [new(basica.Id, ConceptoPlanMonto.AnticipoFinanciero, 2030, 4, Moneda.Pesos, 200m),
             new(basica.Id, ConceptoPlanMonto.Mensual, 2030, 3, Moneda.Pesos, 800m)],
            vm.Periodos, vm.RowVersion);
        var tardio = await servicio.BuildVmAsync(obraId);
        Assert.Equal(new PeriodoVM(2030, 4), tardio.Bloques.Single().MesAnticipo());

        // ...pero el cambio de paso a Cargada lo rechaza y el plan sigue Pendiente.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.MarcarCargadaAsync(plan.Id));
        Assert.Contains("primer mes del plan (03/2030)", ex.Message);
        Assert.Contains("Obra Básica: 04/2030", ex.Message);
        Assert.Equal(EstadoPlanificacion.Pendiente, (await servicio.BuildVmAsync(obraId)).Estado);

        // Corregido a feb/2030 (antes del inicio): guarda y pasa a Cargada.
        await servicio.GuardarGrillaAsync(plan.Id,
            [new(basica.Id, ConceptoPlanMonto.AnticipoFinanciero, 2030, 2, Moneda.Pesos, 200m),
             new(basica.Id, ConceptoPlanMonto.Mensual, 2030, 3, Moneda.Pesos, 800m)],
            tardio.Periodos, tardio.RowVersion);
        await servicio.MarcarCargadaAsync(plan.Id);

        var cargado = await servicio.BuildVmAsync(obraId);
        Assert.Equal(new PeriodoVM(2030, 2), cargado.Bloques.Single().MesAnticipo());
        Assert.Equal(EstadoPlanificacion.Cargada, cargado.Estado);
    }

    // ── La Obra Básica se controla contra el presupuesto de la obra ───────────

    [Fact]
    public async Task Basica_BalanceaContraElAdjudicadoOSiNoElOficial_IgnorandoFilasViejas()
    {
        // Oficial 1000 (+ US$ 50), adjudicado 800 solo en pesos: manda el adjudicado en
        // todas las monedas (el US$ oficial no aplica: la referencia en USD es 0).
        int obraId;
        await using (var db = fx.CrearContexto())
        {
            var obra = new Obra
            {
                Nombre = $"Obra plan {Guid.NewGuid():N}", NumeroLicitacion = "LP PLAN",
                PresupuestoOficial = 1000m, PresupuestoOficialUSD = 50m, PresupuestoAdjudicado = 800m,
                FechaFinalContrato = new DateTime(2030, 12, 1)
            };
            db.Obras.Add(obra);
            await db.SaveChangesAsync();
            obraId = obra.Id;
        }
        var (servicio, _) = Crear(Roles.Admin);
        var plan = await servicio.GetOrCreateAsync(obraId);
        var basica = await servicio.AgregarAutorizanteAsync(plan.Id, TipoAutorizante.Basica, null, null);

        // Una celda de Monto Autorizado para la básica (la grilla ya no la manda) se
        // descarta; y una fila vieja persistida antes de la regla se ignora en el balance.
        await servicio.GuardarGrillaAsync(plan.Id,
            [new PlanMontoInput(basica.Id, ConceptoPlanMonto.MontoAutorizado, null, null, Moneda.Pesos, 5000m),
             new PlanMontoInput(basica.Id, ConceptoPlanMonto.Mensual, 2030, 1, Moneda.Pesos, 800m)],
            PeriodosTest);
        await using (var db = fx.CrearContexto())
        {
            Assert.Equal(1, await db.PlanMontos.CountAsync(m => m.AutorizanteId == basica.Id));
            db.PlanMontos.Add(new PlanMonto
            {
                AutorizanteId = basica.Id, Concepto = ConceptoPlanMonto.MontoAutorizado,
                Moneda = Moneda.Pesos, Monto = 5000m
            });
            await db.SaveChangesAsync();
        }

        var vm = await servicio.BuildVmAsync(obraId);
        Assert.Equal(800m, vm.PresupuestoReferencia(Moneda.Pesos));
        Assert.Equal(0m, vm.PresupuestoReferencia(Moneda.USD));
        Assert.Equal("Presupuesto adjudicado", vm.PresupuestoEtiqueta);
        Assert.Equal(800m, vm.Bloques.Single().Valor(ConceptoPlanMonto.MontoAutorizado, null, null, Moneda.Pesos));
        Assert.Equal(0m, vm.Bloques.Single().Valor(ConceptoPlanMonto.MontoAutorizado, null, null, Moneda.USD));

        await servicio.MarcarCargadaAsync(plan.Id); // balancea: 800 planificados contra 800 adjudicados
        await servicio.AprobarAsync(plan.Id);
        await servicio.TomarConocimientoAsync(plan.Id);

        await using var db2 = fx.CrearContexto();
        var snap = await db2.PlanificacionSnapshots.Include(s => s.Montos).SingleAsync(s => s.ObraId == obraId);
        Assert.Single(snap.Montos, m => m.Concepto == ConceptoPlanMonto.MontoAutorizado && m.Monto == 800m);
        Assert.DoesNotContain(snap.Montos, m => m.Monto == 5000m);

        // Sin adjudicado, manda el oficial: la curva de 800 ya no balancea contra 1000.
        await servicio.EnviarARevisionAsync(plan.Id, "se anuló la adjudicación", null);
        await using (var db = fx.CrearContexto())
        {
            (await db.Obras.SingleAsync(o => o.Id == obraId)).PresupuestoAdjudicado = null;
            await db.SaveChangesAsync();
        }
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.MarcarCargadaAsync(plan.Id));
        Assert.Contains("falta planificar 200", ex.Message);
    }

    [Fact]
    public async Task ObraSinPresupuesto_SeGuardaPeroNoPasaACargada()
    {
        var obraId = await CrearObraAsync(); // presupuesto 0: obra anterior a la columna
        var (servicio, _) = Crear(Roles.Admin);
        var plan = await servicio.GetOrCreateAsync(obraId);
        var basica = await servicio.AgregarAutorizanteAsync(plan.Id, TipoAutorizante.Basica, null, null);
        var avisos = await servicio.GuardarGrillaAsync(plan.Id,
            [new PlanMontoInput(basica.Id, ConceptoPlanMonto.Mensual, 2030, 1, Moneda.Pesos, 100m)],
            PeriodosTest);
        Assert.Contains(avisos, a => a.Contains("no tiene presupuesto oficial"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.MarcarCargadaAsync(plan.Id));
        Assert.Contains("no tiene presupuesto oficial", ex.Message);
    }

    [Fact]
    public async Task TomarConocimiento_PlanAprobadoDesbalanceadoPorFuera_Rechaza()
    {
        // Backstop: datos desbalanceados por fuera del guardado (la grilla ya no se
        // edita en Aprobada) no pueden convertirse en la versión oficial del mes.
        var (servicio, _) = Crear(Roles.Admin);
        var (_, plan, basica) = await CrearPlanBalanceadoAsync(servicio);
        await servicio.MarcarCargadaAsync(plan.Id);
        await servicio.AprobarAsync(plan.Id);

        await using (var db = fx.CrearContexto())
        {
            db.PlanMontos.Add(new PlanMonto
            {
                AutorizanteId = basica.Id, Concepto = ConceptoPlanMonto.Mensual,
                Anio = 2030, Mes = 2, Moneda = Moneda.Pesos, Monto = 200m
            });
            await db.SaveChangesAsync();
        }

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.TomarConocimientoAsync(plan.Id));
        Assert.Contains("Autorizado vs Planificado", ex.Message);
        await using var db2 = fx.CrearContexto();
        Assert.Null((await db2.Planificaciones.SingleAsync(p => p.Id == plan.Id)).FechaTomaConocimiento);
    }

    [Fact]
    public async Task CambioDePaso_ObraQuePasoAProyectada_Rechaza()
    {
        // Pestaña abierta antes de que la obra saliera de Planificación: el servicio
        // no deja avanzar el plan aunque la página no lo haya detectado.
        var (servicio, _) = Crear(Roles.Admin);
        var (obraId, plan, _) = await CrearPlanBalanceadoAsync(servicio);
        await using (var db = fx.CrearContexto())
        {
            (await db.Obras.SingleAsync(o => o.Id == obraId)).FechaFinalContrato = null;
            await db.SaveChangesAsync();
        }

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.MarcarCargadaAsync(plan.Id));
        Assert.Contains("Proyectada", ex.Message);
    }

    // ── Obras "Proyectadas" (sin plazo ni expediente) quedan fuera del módulo ──

    [Fact]
    public async Task Listado_ExcluyeObrasProyectadas()
    {
        var proyectadaId = await CrearObraAsync(proyectada: true);
        int vigenteId;
        await using (var db = fx.CrearContexto())
        {
            var obra = new Obra
            {
                Nombre = $"Obra plan {Guid.NewGuid():N}", NumeroLicitacion = "LP PLAN",
                FechaFinalContrato = DateTime.Today.AddYears(1)
            };
            db.Obras.Add(obra);
            await db.SaveChangesAsync();
            vigenteId = obra.Id;
        }
        var (servicio, _) = Crear(Roles.Admin);

        var listado = await servicio.ListadoAsync();

        Assert.Contains(listado.Obras, o => o.Id == vigenteId);
        Assert.DoesNotContain(listado.Obras, o => o.Id == proyectadaId);
    }

    [Fact]
    public async Task VerificarAcceso_ObraProyectada_LaRechaza()
    {
        var proyectadaId = await CrearObraAsync(proyectada: true);
        var (servicio, _) = Crear(Roles.Admin);

        Assert.Equal(AccesoPlanObra.Proyectada, await servicio.VerificarAccesoObraAsync(proyectadaId));
        Assert.Equal(AccesoPlanObra.NoEncontrada, await servicio.VerificarAccesoObraAsync(-1));
    }
}
