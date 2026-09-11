using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SIGO.Data;
using SIGO.Models;
using SIGO.Models.Enums;
using SIGO.Models.ViewModels;
using SIGO.Services.Validaciones;
using static System.Net.WebUtility;

namespace SIGO.Services;

/// <summary>
/// Lógica del módulo de Planificación (contexto separado; solo comparte <see cref="Obra"/>).
/// Un único plan editable por obra: carga de la grilla, totales por moneda y la máquina de
/// estados del circuito (Pendiente → Cargada → Aprobada, con loop EnRevision). Los mails
/// de cada transición se enganchan en la Fase 4.
/// </summary>
public class PlanificacionService(
    IDbContextFactory<AppDbContext> dbFactory,
    ICurrentUser currentUser,
    IEmailSender emailSender,
    IPlanNotificationRecipients destinatarios,
    IOptionsMonitor<CorreoOptions> correo,
    ILogger<PlanificacionService> logger)
{
    /// <summary>Defensa en profundidad (ver <see cref="Roles.Exigir"/>): cada mutación exige el rol que el circuito define (Director carga, Gerente aprueba/revisa, Admin todo).</summary>
    private void ExigirRol(string rolesPermitidos, string accion) => Roles.Exigir(currentUser.IsInRole, rolesPermitidos, accion);

    /// <summary>
    /// Un Director sin rol de alcance total (Admin/Gerente/Presupuesto) solo opera las
    /// obras que tiene asignadas como director (Obra.DirectorUsuario).
    /// </summary>
    private async Task ExigirObraAsignadaAsync(AppDbContext db, int obraId)
    {
        if (!Roles.SoloDirector(currentUser.IsInRole)) return;

        if (!await ObraAsignadaAsync(db, obraId))
            throw new InvalidOperationException("Esta obra no está asignada a tu usuario como director.");
    }

    /// <summary>true si la obra está asignada al usuario actual como director.</summary>
    private async Task<bool> ObraAsignadaAsync(AppDbContext db, int obraId)
    {
        var wu = currentUser.UserId;
        return await db.Obras.AnyAsync(o => o.Id == obraId
            && o.DirectorUsuario != null && o.DirectorUsuario.WindowsUser == wu);
    }
    /// <summary>Trae el plan de la obra (con bloques y montos) o lo crea en Pendiente.</summary>
    public async Task<Planificacion> GetOrCreateAsync(int obraId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await GetOrCreateAsync(db, obraId);
    }

    private static async Task<Planificacion> GetOrCreateAsync(AppDbContext db, int obraId)
    {
        var plan = await db.Planificaciones
            .Include(p => p.Autorizantes).ThenInclude(a => a.Montos)
            .FirstOrDefaultAsync(p => p.ObraId == obraId);

        if (plan is null)
        {
            plan = new Planificacion { ObraId = obraId, Estado = EstadoPlanificacion.Pendiente };
            db.Planificaciones.Add(plan);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Otro circuito creó el plan en paralelo (índice único sobre ObraId):
                // se usa el existente en vez de propagar el error al usuario.
                db.Entry(plan).State = EntityState.Detached;
                plan = await db.Planificaciones
                    .Include(p => p.Autorizantes).ThenInclude(a => a.Montos)
                    .FirstAsync(p => p.ObraId == obraId);
            }
        }

        return plan;
    }

    /// <summary>
    /// Construye el VM de la grilla: bloques × períodos × monedas.
    /// Los subtotales y totales los calcula la página sobre los valores en edición.
    /// </summary>
    public async Task<PlanificacionVM> BuildVmAsync(int obraId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var obra = await db.Obras.FirstOrThrowAsync(o => o.Id == obraId, "Obra no encontrada.");

        var plan = await GetOrCreateAsync(db, obraId);

        var vm = new PlanificacionVM
        {
            PlanificacionId = plan.Id,
            Estado = plan.Estado,
            FechaTomaConocimiento = plan.FechaTomaConocimiento,
            MotivoRevision = plan.MotivoRevision,
            Correcciones = plan.Correcciones,
            ObraFinalizada = plan.ObraFinalizada,
            ObraId = obra.Id,
            ObraNombre = obra.Nombre,
            NumeroLicitacion = obra.NumeroLicitacion,
            Presupuestos = obra.Presupuestos,
            RowVersion = plan.RowVersion,
            Periodos = PeriodosDe(obra)
        };

        foreach (var aut in plan.Autorizantes.OrderBy(a => a.Tipo).ThenBy(a => a.Numero))
        {
            var bloque = new BloqueAutorizanteVM
            {
                AutorizanteId = aut.Id,
                Tipo = aut.Tipo,
                Numero = aut.Numero,
                Denominacion = aut.Denominacion,
                Titulo = TituloBloque(aut),
                Valores = MontosEfectivos(aut, vm.Presupuestos).ToDictionary(
                    m => (m.Concepto, m.Anio, m.Mes, m.Moneda),
                    m => m.Monto)
            };

            vm.Bloques.Add(bloque);
        }

        // Años anuales huérfanos: curva CalculoAnual guardada para un año que ya no es
        // bucket de la cola — típicamente se EXTENDIÓ el plazo y ese año pasó a
        // renderizarse mensual. Sin celda, el monto seguía contando en el balance sin
        // forma de editarlo (la única maniobra era retroceder la fecha de fin, vaciar y
        // re-extender). Se agregan como buckets renderizados: la fila vuelve editable,
        // llevarla a 0 la elimina, y el aviso de fuera-de-horizonte deja de listarla.
        // Decisión 2026-07-31: fila editable, sin redistribución automática.
        var aniosBucket = vm.Periodos.Where(p => p.EsAnual).Select(p => p.Anio).ToHashSet();
        var huerfanos = vm.Bloques
            .SelectMany(b => b.Valores.Keys)
            .Where(k => k.Concepto == ConceptoPlanMonto.CalculoAnual
                     && k.Anio.HasValue && !aniosBucket.Contains(k.Anio.Value))
            .Select(k => k.Anio!.Value)
            .Distinct()
            .ToList();
        if (huerfanos.Count > 0)
        {
            // La cola anual queda ordenada por año (el huérfano suele ser anterior a los
            // buckets de la cola normal).
            var cola = vm.Periodos.Where(p => p.EsAnual)
                .Concat(huerfanos.Select(a => new PeriodoVM(a, null)))
                .OrderBy(p => p.Anio);
            vm.Periodos = vm.Periodos.Where(p => !p.EsAnual).Concat(cola).ToList();
        }

        return vm;
    }

    // ── Lecturas (solo consulta: sin ExigirRol; las páginas ya están detrás de [Authorize]) ──

    /// <summary>
    /// Listado para la página de planificación: obras visibles (un Director sin rol de
    /// alcance total ve solo las suyas), el estado del plan de cada una y los meses con
    /// versiones tomadas (cortes disponibles).
    /// </summary>
    public async Task<PlanListadoVM> ListadoAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var soloDirector = Roles.SoloDirector(currentUser.IsInRole);
        // Una obra "Proyectada" (sin plazo ni expediente) todavía no se planifica.
        var query = db.Obras.AsNoTracking().Where(ObraValidator.EnPlanificacion(DateTime.Today));
        if (soloDirector)
        {
            var wu = currentUser.UserId;
            query = query.Where(o => o.DirectorUsuario != null && o.DirectorUsuario.WindowsUser == wu);
        }

        var obras = await query.OrderBy(o => o.Nombre)
            .Select(o => new PlanObraRowVM
            {
                Id = o.Id,
                Nombre = o.Nombre,
                NumeroLicitacion = o.NumeroLicitacion,
                // Preferir el usuario asignado; el texto libre es el interino legado.
                DirectorObra = o.DirectorUsuario != null ? o.DirectorUsuario.Nombre : o.DirectorObra
            })
            .ToListAsync();

        // Solo los planes de las obras listadas: un director no necesita (ni debe
        // cargar en memoria) los del resto.
        var obraIds = obras.Select(o => o.Id).ToList();
        var planes = await db.Planificaciones.AsNoTracking()
            .Where(p => obraIds.Contains(p.ObraId))
            .Select(p => new { p.ObraId, p.Estado, p.FechaTomaConocimiento, p.ObraFinalizada })
            .ToDictionaryAsync(p => p.ObraId);
        foreach (var fila in obras)
        {
            if (!planes.TryGetValue(fila.Id, out var p)) continue;
            fila.EstadoPlan = p.Estado;
            fila.FechaTomaConocimiento = p.FechaTomaConocimiento;
            fila.ObraFinalizada = p.ObraFinalizada;
        }

        // Cortes disponibles: los meses con al menos una versión tomada.
        var meses = await db.PlanificacionSnapshots.AsNoTracking()
            .Where(s => obraIds.Contains(s.ObraId))
            .Select(s => new { s.Anio, s.Mes })
            .Distinct()
            .OrderByDescending(s => s.Anio).ThenByDescending(s => s.Mes)
            .ToListAsync();

        return new PlanListadoVM
        {
            SoloDirector = soloDirector,
            Obras = obras,
            Cortes = meses.Select(m => new CorteMesVM(m.Anio, m.Mes)).ToList()
        };
    }

    /// <summary>
    /// Versión vigente de cada obra a un corte mensual (as-of: la última versión hasta
    /// el corte inclusive). Clave: ObraId; sin entrada = sin versión a ese corte.
    /// </summary>
    public async Task<Dictionary<int, VersionCorteVM>> VersionesAlCorteAsync(
        IReadOnlyCollection<int> obraIds, int anio, int mes)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var snaps = await db.PlanificacionSnapshots.AsNoTracking()
            .Where(s => obraIds.Contains(s.ObraId)
                && (s.Anio < anio || (s.Anio == anio && s.Mes <= mes)))
            .Select(s => new { s.ObraId, s.Anio, s.Mes, s.FechaTomaConocimiento })
            .ToListAsync();

        return snaps
            .GroupBy(s => s.ObraId)
            .ToDictionary(g => g.Key, g =>
            {
                var u = g.OrderByDescending(s => s.Anio).ThenByDescending(s => s.Mes).First();
                return new VersionCorteVM(u.Anio, u.Mes, u.FechaTomaConocimiento);
            });
    }

    /// <summary>
    /// Veredicto de acceso a la planificación de una obra, en el mismo orden que la UI
    /// lo informa: primero existencia, después la asignación al director (espejo de
    /// <see cref="ExigirObraAsignadaAsync"/>, que además lo exige en cada mutación).
    /// </summary>
    public async Task<AccesoPlanObra> VerificarAccesoObraAsync(int obraId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        // Una sola lectura de la obra: existencia, estado computado (misma regla que el
        // predicado SQL del listado, ver ObraValidatorTests) y asignación al director.
        var wu = currentUser.UserId;
        var o = await db.Obras.AsNoTracking().Where(o => o.Id == obraId)
            .Select(o => new
            {
                o.Antecedentes, o.FechaActaInicio, o.FechaFinalContrato,
                Asignada = o.DirectorUsuario != null && o.DirectorUsuario.WindowsUser == wu
            })
            .FirstOrDefaultAsync();
        if (o is null)
            return AccesoPlanObra.NoEncontrada;

        if (ObraValidator.Estado(o.Antecedentes, o.FechaActaInicio, o.FechaFinalContrato, DateTime.Today) == "Proyectada")
            return AccesoPlanObra.Proyectada;

        if (Roles.SoloDirector(currentUser.IsInRole) && !o.Asignada)
            return AccesoPlanObra.NoAsignada;

        return AccesoPlanObra.Ok;
    }

    /// <summary>Versiones mensuales (snapshots) del plan de una obra, más reciente primero.</summary>
    public async Task<List<PlanVersionVM>> VersionesDeObraAsync(int obraId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        return await db.PlanificacionSnapshots.AsNoTracking()
            .Where(s => s.ObraId == obraId)
            .OrderByDescending(s => s.Anio).ThenByDescending(s => s.Mes)
            .Select(s => new PlanVersionVM(s.Anio, s.Mes, s.FechaTomaConocimiento, s.TomadaConocimientoPor))
            .ToListAsync();
    }

    // ── Bloques (Autorizantes) ─────────────────────────────────────────────────────

    public async Task<Autorizante> AgregarAutorizanteAsync(int planificacionId, TipoAutorizante tipo, int? numero, string? denominacion)
    {
        ExigirRol(Roles.PlanificacionEdicion, "agregar bloques al plan");
        await using var db = await dbFactory.CreateDbContextAsync();

        var plan = await db.Planificaciones
            .Include(p => p.Autorizantes)
            .FirstOrThrowAsync(p => p.Id == planificacionId, "Planificación no encontrada.");
        await ExigirObraAsignadaAsync(db, plan.ObraId);

        // La básica es única; los adicionales/BED autonumeran si no viene número.
        Validacion.Exigir(PlanificacionValidator.AgregarBloque(
            tipo, plan.Autorizantes.Any(a => a.Tipo == TipoAutorizante.Basica)));

        if (tipo != TipoAutorizante.Basica && numero is null)
            numero = plan.Autorizantes.Where(a => a.Tipo == tipo).Select(a => a.Numero ?? 0).DefaultIfEmpty(0).Max() + 1;

        var aut = new Autorizante
        {
            PlanificacionId = planificacionId,
            Tipo = tipo,
            Numero = tipo == TipoAutorizante.Basica ? null : numero,
            Denominacion = denominacion
        };
        db.Autorizantes.Add(aut);
        await db.SaveChangesAsync();
        return aut;
    }

    /// <summary>
    /// Quita un bloque (y en cascada sus montos). Si el plan queda SIN bloques, el estado
    /// del circuito deja de tener sentido: vuelve a Pendiente y se limpia su rastro.
    /// </summary>
    public async Task EliminarAutorizanteAsync(int autorizanteId)
    {
        ExigirRol(Roles.PlanificacionEdicion, "quitar bloques del plan");
        await using var db = await dbFactory.CreateDbContextAsync();

        var aut = await db.Autorizantes
            .Include(a => a.Planificacion).ThenInclude(p => p.Autorizantes)
            .FirstOrThrowAsync(a => a.Id == autorizanteId, "Bloque no encontrado.");
        await ExigirObraAsignadaAsync(db, aut.Planificacion.ObraId);

        var plan = aut.Planificacion;
        db.Autorizantes.Remove(aut); // cascade borra sus PlanMonto

        // RowVersion del plan también al eliminar bloques: sin esto, borrar el último
        // bloque mientras el gerente marca Cargada (que valida Count > 0 sobre su
        // propia lectura) podía dejar un plan Cargada sin bloques — ahora la segunda
        // operación en confirmar recibe el conflicto de concurrencia.
        db.Entry(plan).Property(p => p.Estado).IsModified = true;

        var quedanBloques = plan.Autorizantes.Any(a => a.Id != autorizanteId);
        if (!quedanBloques && plan.Estado != EstadoPlanificacion.Pendiente)
        {
            plan.Estado = EstadoPlanificacion.Pendiente;
            plan.FechaCarga = null;
            plan.FechaAprobacion = null;
            plan.MotivoRevision = null;
            plan.Correcciones = null;
        }

        await db.SaveChangesAsync();
    }

    // ── Grilla ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reemplaza los montos del plan por los recibidos (solo celdas ≠ 0), acotado a los
    /// períodos que la grilla estaba editando: un monto de un período fuera del horizonte
    /// renderizado (obra muy larga, fin de contrato adelantado) se preserva en vez de
    /// borrarse silenciosamente. El plan es editable en cualquier estado; guardar la
    /// grilla no cambia el estado ni exige las reglas del circuito (presupuesto, balance
    /// y mes del anticipo): las devuelve como avisos sobre lo que quedó guardado, y el
    /// cambio de paso las exige.
    /// </summary>
    public async Task<IReadOnlyList<string>> GuardarGrillaAsync(int planificacionId, IEnumerable<PlanMontoInput> montos,
        IReadOnlyCollection<PeriodoVM> periodosEditados, byte[]? rowVersionSesion = null)
    {
        ExigirRol(Roles.PlanificacionEdicion, "editar la planificación");
        await using var db = await dbFactory.CreateDbContextAsync();

        var plan = await GetConMontosAsync(db, planificacionId);
        await ExigirObraAsignadaAsync(db, plan.ObraId);

        Validacion.Exigir(PlanificacionValidator.GuardarGrilla(plan.Autorizantes.Count));

        var autIds = plan.Autorizantes.Select(a => a.Id).ToHashSet();

        // Token de la sesión + touch del Estado: lost updates entre sesiones y guardados
        // concurrentes de la grilla terminan en conflicto de concurrencia (ver la extensión).
        db.AplicarTokenSesion(plan, rowVersionSesion, p => p.Estado);

        var mesesEditados = periodosEditados.Where(p => !p.EsAnual)
            .Select(p => (p.Anio, Mes: p.Mes!.Value)).ToHashSet();
        var aniosEditados = periodosEditados.Where(p => p.EsAnual)
            .Select(p => p.Anio).ToHashSet();

        bool EnHorizonte(PlanMonto m) =>
            m.Concepto is ConceptoPlanMonto.MontoAutorizado or ConceptoPlanMonto.AnticipoFinanciero
            || (m.Concepto == ConceptoPlanMonto.Mensual && m.Anio.HasValue && m.Mes.HasValue
                && mesesEditados.Contains((m.Anio.Value, m.Mes.Value)))
            || (m.Concepto == ConceptoPlanMonto.CalculoAnual && m.Anio.HasValue
                && aniosEditados.Contains(m.Anio.Value));

        // La Obra Básica no tiene Monto Autorizado propio (es el presupuesto de la obra,
        // ver MontosEfectivos): una celda así recibida se descarta en vez de persistirse.
        var basicaIds = plan.Autorizantes.Where(a => a.Tipo == TipoAutorizante.Basica).Select(a => a.Id).ToHashSet();
        var montosValidos = montos
            .Where(x => x.Monto != 0 && autIds.Contains(x.AutorizanteId)
                && !(x.Concepto == ConceptoPlanMonto.MontoAutorizado && basicaIds.Contains(x.AutorizanteId)))
            .ToList();

        // Las reglas del circuito (presupuesto de la obra cargado, Autorizado vs
        // Planificado = 0 y mes del anticipo ≤ primer mes del plan) NO bloquean el
        // guardado: la grilla se registra tal cual, la página avisa lo incumplido y el
        // cambio de paso a Cargada/Aprobada lo exige.

        foreach (var aut in plan.Autorizantes)
            db.PlanMontos.RemoveRange(aut.Montos.Where(EnHorizonte));

        foreach (var m in montosValidos)
        {
            db.PlanMontos.Add(new PlanMonto
            {
                AutorizanteId = m.AutorizanteId,
                Concepto = m.Concepto,
                Anio = m.Anio,
                Mes = m.Mes,
                Moneda = m.Moneda,
                Monto = m.Monto
            });
        }

        await db.SaveChangesAsync();

        // Avisos sobre lo que quedó registrado: se relee el plan (sin depender del fixup
        // de navegaciones tras borrar/agregar filas) con los mismos datos de obra que
        // usa el cambio de paso.
        db.ChangeTracker.Clear();
        var guardado = await GetConMontosAsync(db, planificacionId);
        return ReglasDelCircuito(guardado, await DatosObraAsync(db, guardado.ObraId));
    }

    /// <summary>
    /// Diferencias Autorizado − Planificado por bloque × moneda sobre las filas de montos
    /// persistidas. Alimentan <see cref="PlanificacionValidator.Balanceado"/>.
    /// </summary>
    private static IEnumerable<(string Bloque, Moneda Moneda, decimal Diferencia)> DiferenciasAutorizadoVsPlanificado(
        IEnumerable<Autorizante> autorizantes,
        ILookup<int, (ConceptoPlanMonto Concepto, Moneda Moneda, decimal Monto)> filas)
    {
        foreach (var aut in autorizantes)
            foreach (var g in filas[aut.Id].GroupBy(f => f.Moneda))
            {
                var autorizado = g.Where(f => f.Concepto == ConceptoPlanMonto.MontoAutorizado).Sum(f => f.Monto);
                var planificado = g.Where(f => f.Concepto != ConceptoPlanMonto.MontoAutorizado).Sum(f => f.Monto);
                if (autorizado != planificado)
                    yield return (TituloBloque(aut), g.Key, autorizado - planificado);
            }
    }

    /// <summary>
    /// Mes asignado al anticipo de cada bloque (el primero por bloque, como
    /// <see cref="BloqueAutorizanteVM.MesAnticipo"/>; null si no tiene mes), a partir
    /// de filas de montos. Alimenta <see cref="PlanificacionValidator.MesAnticipo"/>.
    /// </summary>
    private static IEnumerable<(string Bloque, int? Anio, int? Mes)> MesesAnticipo(
        IEnumerable<Autorizante> autorizantes,
        IEnumerable<(int AutorizanteId, ConceptoPlanMonto Concepto, int? Anio, int? Mes)> filas)
    {
        var porBloque = filas
            .Where(f => f.Concepto == ConceptoPlanMonto.AnticipoFinanciero && f.Anio.HasValue && f.Mes.HasValue)
            .GroupBy(f => f.AutorizanteId)
            .ToDictionary(g => g.Key, g => g.OrderBy(f => f.Anio).ThenBy(f => f.Mes).First());
        foreach (var aut in autorizantes)
            yield return porBloque.TryGetValue(aut.Id, out var f)
                ? (TituloBloque(aut), f.Anio, f.Mes)
                : (TituloBloque(aut), null, null);
    }

    /// <summary>Filas efectivas de los bloques (con Montos ya cargados por Include) para el chequeo de balance.</summary>
    private static ILookup<int, (ConceptoPlanMonto Concepto, Moneda Moneda, decimal Monto)> FilasEfectivas(
        IEnumerable<Autorizante> autorizantes, PresupuestosObra presupuestos) =>
        autorizantes.SelectMany(a => MontosEfectivos(a, presupuestos)
                .Select(m => (AutorizanteId: a.Id, m.Concepto, m.Moneda, m.Monto)))
            .ToLookup(f => f.AutorizanteId, f => (f.Concepto, f.Moneda, f.Monto));

    /// <summary>
    /// Montos efectivos de un bloque (balance, VM, snapshot y export): los persistidos,
    /// salvo que la Obra Básica no tiene Monto Autorizado propio: su autorizado es el
    /// presupuesto de la obra que aplica (adjudicado u oficial), moneda por moneda, y
    /// cualquier fila MontoAutorizado persistida de la básica (datos previos a la regla)
    /// se ignora. ÚNICA implementación de esa sustitución.
    /// </summary>
    public static IEnumerable<MontoEfectivoVM> MontosEfectivos(Autorizante a, PresupuestosObra presupuestos)
    {
        if (a.Tipo != TipoAutorizante.Basica)
        {
            foreach (var m in a.Montos)
                yield return new MontoEfectivoVM(m.Concepto, m.Anio, m.Mes, m.Moneda, m.Monto);
            yield break;
        }

        foreach (var m in a.Montos.Where(m => m.Concepto != ConceptoPlanMonto.MontoAutorizado))
            yield return new MontoEfectivoVM(m.Concepto, m.Anio, m.Mes, m.Moneda, m.Monto);
        foreach (var moneda in Enum.GetValues<Moneda>())
        {
            var referencia = presupuestos.Referencia(moneda);
            if (referencia != 0)
                yield return new MontoEfectivoVM(ConceptoPlanMonto.MontoAutorizado, null, null, moneda, referencia);
        }
    }

    // ── Máquina de estados (con los mails del circuito) ─────────────────────────────

    /// <summary>Director deja el plan como Cargada. Mail 2: al/los Gerente(s).</summary>
    public async Task MarcarCargadaAsync(int planificacionId)
    {
        ExigirRol(Roles.PlanificacionCarga, "marcar el plan como Cargada");
        await using var db = await dbFactory.CreateDbContextAsync();

        var plan = await GetConMontosAsync(db, planificacionId);
        await ExigirObraAsignadaAsync(db, plan.ObraId);

        Validacion.Exigir(PlanificacionValidator.MarcarCargada(plan.Estado, plan.Autorizantes.Count));
        var obra = (await ExigirReglasDelCircuitoAsync(db, plan)).NombreCompleto;

        plan.Estado = EstadoPlanificacion.Cargada;
        plan.FechaCarga = DateTime.UtcNow;
        plan.MotivoRevision = null;
        plan.Correcciones = null;
        await db.SaveChangesAsync();

        await NotificarAsync("Cargada", async () => await emailSender.EnviarAsync(
            await destinatarios.ConRolAsync(RolUsuario.Gerente),
            $"Planificación cargada — {obra}",
            CuerpoMail($"{Quien()} marcó como <b>Cargada</b> la planificación de la obra <b>{HtmlEncode(obra)}</b>.",
                       "Revisala para aprobarla o enviarla a revisión.", plan.ObraId)));
    }

    /// <summary>Gerente devuelve el plan a revisión con un motivo. Mail al Director de la obra.</summary>
    public async Task EnviarARevisionAsync(int planificacionId, string motivo, string? correcciones)
    {
        ExigirRol(Roles.PlanificacionAprobacion, "enviar el plan a revisión");
        await using var db = await dbFactory.CreateDbContextAsync();

        var plan = await GetByIdAsync(db, planificacionId);
        Validacion.Exigir(PlanificacionValidator.EnviarARevision(plan.Estado, motivo));

        var obra = await NombreObraAsync(db, plan.ObraId);
        motivo = motivo.Trim();
        correcciones = string.IsNullOrWhiteSpace(correcciones) ? null : correcciones.Trim();

        plan.Estado = EstadoPlanificacion.EnRevision;
        plan.MotivoRevision = motivo;
        plan.Correcciones = correcciones;
        plan.FechaAprobacion = null;
        // El plan deja de estar Aprobado: la toma de conocimiento previa ya no vale
        // y la re-aprobación va a requerir una nueva (que reemplaza el snapshot del mes).
        plan.FechaTomaConocimiento = null;
        plan.TomadaConocimientoPor = null;
        await db.SaveChangesAsync();

        var detalle = $"<p><b>Motivo:</b> {HtmlEncode(motivo)}</p>" +
                      (string.IsNullOrWhiteSpace(correcciones) ? "" : $"<p><b>Correcciones:</b> {HtmlEncode(correcciones)}</p>");
        await NotificarAsync("EnRevisión", async () => await emailSender.EnviarAsync(
            await destinatarios.DirectorDeObraAsync(plan.ObraId),
            $"Planificación en revisión — {obra}",
            CuerpoMail($"{Quien()} envió a <b>revisión</b> la planificación de la obra <b>{HtmlEncode(obra)}</b>.{detalle}",
                       "Corregila y volvé a marcarla como Cargada.", plan.ObraId)));
    }

    /// <summary>Gerente aprueba el plan. Mail 3: a Presupuesto (toma de conocimiento).</summary>
    public async Task AprobarAsync(int planificacionId)
    {
        ExigirRol(Roles.PlanificacionAprobacion, "aprobar el plan");
        await using var db = await dbFactory.CreateDbContextAsync();

        var plan = await GetConMontosAsync(db, planificacionId);
        Validacion.Exigir(PlanificacionValidator.Aprobar(plan.Estado));
        var obra = (await ExigirReglasDelCircuitoAsync(db, plan)).NombreCompleto;

        plan.Estado = EstadoPlanificacion.Aprobada;
        plan.FechaAprobacion = DateTime.UtcNow;
        await db.SaveChangesAsync();

        await NotificarAsync("Aprobada", async () => await emailSender.EnviarAsync(
            await destinatarios.ConRolAsync(RolUsuario.Presupuesto),
            $"Planificación aprobada — {obra}",
            CuerpoMail($"{Quien()} <b>aprobó</b> la planificación de la obra <b>{HtmlEncode(obra)}</b>.",
                       "Tomá conocimiento del plan en el sistema: eso registra la versión del mes.", plan.ObraId)));
    }

    /// <summary>
    /// Presupuesto toma conocimiento de un plan Aprobado y eso genera el snapshot del
    /// mes (la "versión" mensual del circuito; sin toma no hay foto). Una segunda toma
    /// en el mismo mes —tras un loop de revisión— reemplaza la foto: vale la última
    /// versión conocida del mes. Flag y snapshot se confirman en un único SaveChanges.
    /// </summary>
    public async Task TomarConocimientoAsync(int planificacionId)
    {
        ExigirRol(Roles.PlanificacionConocimiento, "tomar conocimiento del plan");
        await using var db = await dbFactory.CreateDbContextAsync();

        var plan = await GetConMontosAsync(db, planificacionId);

        Validacion.Exigir(PlanificacionValidator.TomarConocimiento(plan.Estado, plan.FechaTomaConocimiento));

        // La foto es la versión oficial del mes: backstop de las reglas del circuito
        // (la grilla ya no se edita en Aprobada, pero puede haber datos previos a la regla).
        var obra = await ExigirReglasDelCircuitoAsync(db, plan);

        var ahora = DateTime.Now;

        var previo = await db.PlanificacionSnapshots
            .FirstOrDefaultAsync(s => s.ObraId == plan.ObraId && s.Anio == ahora.Year && s.Mes == ahora.Month);
        if (previo is not null)
            db.PlanificacionSnapshots.Remove(previo); // cascade borra sus montos

        var snapshot = new PlanificacionSnapshot
        {
            ObraId = plan.ObraId,
            Anio = ahora.Year,
            Mes = ahora.Month,
            FechaTomaConocimiento = DateTime.UtcNow,
            TomadaConocimientoPor = currentUser.UserId,
            // La foto lleva los montos efectivos: el autorizado de la básica es el
            // presupuesto de la obra al momento de la toma (queda denormalizado como
            // una fila MontoAutorizado más, para que el export de versiones no cambie).
            Montos = plan.Autorizantes
                .SelectMany(a => MontosEfectivos(a, obra.Presupuestos).Select(m => new PlanMontoSnapshot
                {
                    Tipo = a.Tipo,
                    Numero = a.Numero,
                    Denominacion = a.Denominacion,
                    Concepto = m.Concepto,
                    Anio = m.Anio,
                    Mes = m.Mes,
                    Moneda = m.Moneda,
                    Monto = m.Monto
                }))
                .ToList()
        };
        db.PlanificacionSnapshots.Add(snapshot);

        plan.FechaTomaConocimiento = snapshot.FechaTomaConocimiento;
        plan.TomadaConocimientoPor = currentUser.UserId;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Marca (o desmarca) la obra como finalizada a los efectos de la planificación:
    /// fuera del ciclo mensual (rollover y digest). No toca el estado del plan ni su
    /// historia de snapshots.
    /// </summary>
    public async Task MarcarObraFinalizadaAsync(int planificacionId, bool finalizada)
    {
        ExigirRol(Roles.PlanificacionEdicion, "marcar la obra como finalizada");
        await using var db = await dbFactory.CreateDbContextAsync();

        var plan = await GetByIdAsync(db, planificacionId);
        await ExigirObraAsignadaAsync(db, plan.ObraId);

        plan.ObraFinalizada = finalizada;
        await db.SaveChangesAsync();
    }

    // ── Notificaciones ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Envío best-effort DESPUÉS del SaveChanges: la transición ya quedó firme y un
    /// fallo de SMTP no debe revertirla ni romper la UI — solo se loguea.
    /// </summary>
    private async Task NotificarAsync(string transicion, Func<Task> envio)
    {
        try { await envio(); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falló el mail de la transición {Transicion} de planificación.", transicion);
        }
    }

    private string Quien() => HtmlEncode(currentUser.UserId ?? "Un usuario");

    private string CuerpoMail(string mensaje, string indicacion, int obraId)
    {
        var url = PlanMails.UrlPlan(correo.CurrentValue.BaseUrl, obraId);
        var acceso = url is null ? PlanMails.AccesoFallback : $"<p><a href=\"{url}\">Abrir la planificación</a></p>";
        return $"<p>{mensaje}</p><p>{indicacion}</p>{acceso}{PlanMails.Footer}";
    }

    private static async Task<string> NombreObraAsync(AppDbContext db, int obraId) =>
        await db.Obras.Where(o => o.Id == obraId)
            .Select(o => o.Nombre + " (" + o.NumeroLicitacion + ")")
            .FirstOrDefaultAsync() ?? $"Obra #{obraId}";

    // ── helpers ──────────────────────────────────────────────────────────────────────

    private static Task<Planificacion> GetByIdAsync(AppDbContext db, int planificacionId) =>
        db.Planificaciones.FirstOrThrowAsync(p => p.Id == planificacionId, "Planificación no encontrada.");

    /// <summary>Como <see cref="GetByIdAsync"/> pero con bloques y montos cargados (grilla, circuito y snapshot).</summary>
    private static Task<Planificacion> GetConMontosAsync(AppDbContext db, int planificacionId) =>
        db.Planificaciones
            .Include(p => p.Autorizantes).ThenInclude(a => a.Montos)
            .FirstOrThrowAsync(p => p.Id == planificacionId, "Planificación no encontrada.");

    /// <summary>
    /// Reglas del circuito sobre el plan persistido, en orden: presupuesto de la obra
    /// cargado, Autorizado vs Planificado = 0 (la básica contra ese presupuesto) y mes del
    /// anticipo ≤ primer mes del plan (solo si la obra tiene fecha de inicio: sin acta ni
    /// contrato el horizonte arranca en el mes actual y la regla cambiaría sola cada mes).
    /// ÚNICA implementación: el guardado las devuelve como avisos y el cambio de paso
    /// (Cargada / Aprobada / toma de conocimiento) las exige vía
    /// <see cref="ExigirReglasDelCircuitoAsync"/>.
    /// </summary>
    private static List<string> ReglasDelCircuito(Planificacion plan, DatosObra obra)
    {
        var reglas = new List<string?>
        {
            PlanificacionValidator.PresupuestoObra(obra.Presupuestos),
            PlanificacionValidator.Balanceado(
                DiferenciasAutorizadoVsPlanificado(plan.Autorizantes, FilasEfectivas(plan.Autorizantes, obra.Presupuestos)))
        };
        if (obra.Inicio is { } inicio)
            reglas.Add(PlanificacionValidator.MesAnticipo(
                MesesAnticipo(plan.Autorizantes, plan.Autorizantes
                    .SelectMany(a => a.Montos.Select(m => (a.Id, m.Concepto, m.Anio, m.Mes)))),
                inicio.Year, inicio.Month));
        return reglas.Where(r => r is not null).Select(r => r!).ToList();
    }

    /// <summary>
    /// Cambio de paso: la obra tiene que seguir participando de Planificación (una
    /// pestaña abierta antes de que pasara a Proyectada no puede avanzar su plan) y las
    /// <see cref="ReglasDelCircuito"/> tienen que cumplirse. Devuelve los datos de la
    /// obra para que el llamador no la vuelva a consultar.
    /// </summary>
    private static async Task<DatosObra> ExigirReglasDelCircuitoAsync(AppDbContext db, Planificacion plan)
    {
        var obra = await DatosObraAsync(db, plan.ObraId);
        Validacion.Exigir(PlanificacionValidator.ObraEnPlanificacion(obra.Estado));
        foreach (var regla in ReglasDelCircuito(plan, obra))
            Validacion.Exigir(regla);
        return obra;
    }

    /// <summary>
    /// Lo que el circuito necesita de la obra: nombre para los mails, estado computado,
    /// primer mes del horizonte (null si no tiene acta ni contrato) y presupuestos.
    /// </summary>
    private sealed record DatosObra(string NombreCompleto, string Estado, DateTime? Inicio, PresupuestosObra Presupuestos);

    /// <summary><see cref="DatosObra"/> leídos por proyección (sin cargar la entidad).</summary>
    private static async Task<DatosObra> DatosObraAsync(AppDbContext db, int obraId)
    {
        var o = await db.Obras.AsNoTracking().Where(o => o.Id == obraId)
            .Select(o => new
            {
                o.Nombre, o.NumeroLicitacion, o.Antecedentes,
                o.FechaActaInicio, o.FechaContrato, o.FechaFinalContrato,
                o.PresupuestoOficial, o.PresupuestoOficialUSD, o.PresupuestoOficialEUR,
                o.PresupuestoAdjudicado, o.PresupuestoAdjudicadoUSD, o.PresupuestoAdjudicadoEUR
            })
            .FirstOrDefaultAsync() ?? throw new EntidadNoEncontradaException("Obra no encontrada.");
        return new DatosObra(
            $"{o.Nombre} ({o.NumeroLicitacion})",
            ObraValidator.Estado(o.Antecedentes, o.FechaActaInicio, o.FechaFinalContrato, DateTime.Today),
            o.FechaActaInicio is null && o.FechaContrato is null ? null : InicioHorizonte(o.FechaActaInicio, o.FechaContrato),
            new PresupuestosObra(o.PresupuestoOficial, o.PresupuestoOficialUSD, o.PresupuestoOficialEUR,
                o.PresupuestoAdjudicado, o.PresupuestoAdjudicadoUSD, o.PresupuestoAdjudicadoEUR));
    }

    /// <summary>
    /// Primer mes del plan: el del acta de inicio, si no el del contrato, si no el mes
    /// actual. ÚNICA implementación: el horizonte de la grilla y la regla del mes del
    /// anticipo parten de acá.
    /// </summary>
    private static DateTime InicioHorizonte(DateTime? fechaActaInicio, DateTime? fechaContrato)
    {
        var hoy = DateTime.Today;
        var inicio = fechaActaInicio ?? fechaContrato ?? hoy;
        return new DateTime(inicio.Year, inicio.Month, 1);
    }

    /// <summary>
    /// Etiqueta del tipo de autorizante para la UI (dropdown y títulos de bloque).
    /// ÚNICA implementación junto a <see cref="TituloBloque"/>. OJO: el export de
    /// Excel usa su propio vocabulario ("Obra Base", ConceptoTexto del controller)
    /// porque replica el formato Flokzu que consume Presupuesto — no unificar.
    /// </summary>
    public static string TipoTexto(TipoAutorizante t) => t switch
    {
        TipoAutorizante.Basica => "Obra Básica",
        TipoAutorizante.Adicional => "Adicional",
        TipoAutorizante.BED => "BED",
        _ => t.ToString()
    };

    /// <summary>Título legible del bloque a partir de su tipo/número/denominación.</summary>
    private static string TituloBloque(Autorizante a)
    {
        var baseTitulo = a.Tipo == TipoAutorizante.Basica
            ? TipoTexto(a.Tipo)
            : $"{TipoTexto(a.Tipo)} N°{a.Numero}";
        return string.IsNullOrWhiteSpace(a.Denominacion) ? baseTitulo : $"{baseTitulo} — {a.Denominacion}";
    }

    /// <summary>
    /// Horizonte del plan: meses desde el inicio de la obra hasta el fin de contrato,
    /// más dos buckets anuales para la cola. Deriva de las fechas de la obra (no hardcodea
    /// como Flokzu). Acota a 120 meses por seguridad ante fechas inconsistentes (los
    /// montos fuera del horizonte no se muestran, pero el guardado ya no los borra).
    /// </summary>
    private static List<PeriodoVM> PeriodosDe(Obra obra)
    {
        var inicio = InicioHorizonte(obra.FechaActaInicio, obra.FechaContrato);

        var fin = obra.FechaFinalContrato ?? inicio.AddMonths(12);
        fin = new DateTime(fin.Year, fin.Month, 1);
        if (fin < inicio) fin = inicio;

        var periodos = new List<PeriodoVM>();
        var cursor = inicio;
        int guard = 0;
        while (cursor <= fin && guard++ < 120)
        {
            periodos.Add(new PeriodoVM(cursor.Year, cursor.Month));
            cursor = cursor.AddMonths(1);
        }

        // Buckets anuales para la cola (dos años posteriores al fin del horizonte mensual)
        periodos.Add(new PeriodoVM(fin.Year + 1, null));
        periodos.Add(new PeriodoVM(fin.Year + 2, null));

        return periodos;
    }
}
