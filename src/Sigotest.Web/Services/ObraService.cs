using Microsoft.EntityFrameworkCore;
using SIGO.Data;
using SIGO.Models;
using SIGO.Models.Enums;
using SIGO.Models.ViewModels;
using SIGO.Services.Validaciones;

namespace SIGO.Services;

/// <summary>
/// ABM de obras detrás de las páginas Blazor del módulo: la UI trabaja con ObraVM y
/// este servicio concentra el acceso a datos, las reglas y la concurrencia.
/// </summary>
public class ObraService(IDbContextFactory<AppDbContext> dbFactory, ICurrentUser currentUser)
{
    /// <summary>Defensa en profundidad (ver <see cref="Roles.Exigir"/>): módulo Certificaciones.</summary>
    private void ExigirRol(string accion) => Roles.Exigir(currentUser.IsInRole, Roles.Certificaciones, accion);

    /// <summary>
    /// Obras para la grilla, ordenadas por nombre. Proyección directa a VM (solo las
    /// columnas que la grilla muestra) + conteo de tablas de ponderación; el RowVersion
    /// viaja para que la eliminación use el token que la página vio.
    /// </summary>
    public async Task<List<ObraVM>> ListarAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Obras.AsNoTracking()
            .OrderBy(o => o.Nombre)
            .Select(o => new ObraVM
            {
                Id = o.Id,
                Nombre = o.Nombre,
                NumeroLicitacion = o.NumeroLicitacion,
                Antecedentes = o.Antecedentes,
                Contratista = o.Contratista,
                FechaActaInicio = o.FechaActaInicio,
                FechaFinalContrato = o.FechaFinalContrato,
                CantidadTablas = o.TablasPonderacion.Count,
                RowVersion = o.RowVersion
            })
            .ToListAsync();
    }

    /// <summary>Obra completa para el formulario de edición. Null si no existe.</summary>
    public async Task<ObraVM?> ObtenerAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var obra = await db.Obras.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id);
        if (obra is null) return null;

        var vm = Mapear(obra, new ObraVM());
        vm.TieneProrrogas = await db.ProrrogasObra.AnyAsync(p => p.ObraId == id);
        return vm;
    }

    /// <summary>
    /// Obra con los resúmenes que muestra el detalle: estructuras (por fecha de
    /// creación) y tablas de ponderación con sus ítems (por número). Null si no existe.
    /// </summary>
    public async Task<ObraDetalleVM?> ObtenerDetalleAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var obra = await db.Obras.AsNoTracking()
            .Include(o => o.TablasPonderacion)
                .ThenInclude(t => t.Items)
            .Include(o => o.EstructurasCostos)
            .Include(o => o.Prorrogas)
            .Include(o => o.DirectorUsuario)
            .FirstOrDefaultAsync(o => o.Id == id);
        if (obra is null) return null;

        var vm = Mapear(obra, new ObraDetalleVM());
        vm.Prorrogas = obra.Prorrogas
            .OrderBy(p => p.Numero)
            .Select(p => new ProrrogaObraVM(p.Id, p.Numero, p.FechaFinAnterior, p.FechaFinNueva,
                p.FechaActo, p.IFActo, p.Observaciones))
            .ToList();
        vm.TieneProrrogas = vm.Prorrogas.Count > 0;
        vm.EstructurasCostos = obra.EstructurasCostos
            .OrderBy(e => e.FechaCreacion)
            .Select(e => new EstructuraObraVM(e.Id, e.Nombre, e.FechaCreacion))
            .ToList();
        vm.TablasPonderacion = obra.TablasPonderacion
            .Select(t => new TablaPonderacionObraVM
            {
                Id = t.Id,
                Nombre = t.Nombre,
                Items = t.Items
                    .OrderBy(i => i.Numero)
                    .Select(i => new ItemPonderacionObraVM(i.Numero, i.Insumo, i.PesoPorcentaje, i.DescripcionINDEC))
                    .ToList()
            })
            .ToList();
        return vm;
    }

    /// <summary>
    /// Candidatos a director: usuarios activos con rol Director. Si la obra ya tiene
    /// asignado uno que perdió el rol o se desactivó, se conserva en la lista para
    /// no romper el valor guardado al editar otra cosa.
    /// </summary>
    public async Task<List<DirectorCandidatoVM>> CandidatosDirectorAsync(int? obraId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Usuarios.AsNoTracking()
            .Where(u => (u.Activo && u.Roles.Any(r => r.Rol == RolUsuario.Director))
                        || (obraId != null && db.Obras.Any(o => o.Id == obraId && o.DirectorUsuarioId == u.Id)))
            .OrderBy(u => u.Nombre)
            .Select(u => new DirectorCandidatoVM(u.Id, u.Nombre))
            .ToListAsync();
    }

    /// <summary>Alta de una obra con los datos del formulario.</summary>
    public async Task CrearAsync(ObraVM vm)
    {
        ExigirRol("crear la obra");
        Validacion.Exigir(ObraValidator.Guardar(vm.Nombre, vm.NumeroLicitacion, vm.Presupuestos));

        await using var db = await dbFactory.CreateDbContextAsync();
        var obra = new Obra();
        Aplicar(vm, obra);
        db.Obras.Add(obra);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Actualiza una obra existente con el token de concurrencia de la sesión de
    /// edición: dos usuarios con el mismo formulario abierto no se pisan en silencio.
    /// </summary>
    public async Task ActualizarAsync(ObraVM vm)
    {
        ExigirRol("modificar la obra");
        Validacion.Exigir(ObraValidator.Guardar(vm.Nombre, vm.NumeroLicitacion, vm.Presupuestos));

        await using var db = await dbFactory.CreateDbContextAsync();
        var obra = await db.Obras.FirstOrDefaultAsync(o => o.Id == vm.Id)
            // La fila ya no existe: mismo desenlace que producía el UPDATE de la página
            // (0 filas afectadas) — Persistencia lo notifica como conflicto de concurrencia
            // ("recargá la página"), que es exactamente lo que corresponde hacer.
            ?? throw new DbUpdateConcurrencyException(
                "La obra fue eliminada por otro usuario mientras la editabas.");

        // Con prórrogas registradas la fecha de fin vigente la gobiernan ellas: el
        // formulario la muestra deshabilitada y acá se conserva la de la DB (espejo
        // server de esa regla, por si el VM llega de otro lado).
        var finVigente = obra.FechaFinalContrato;
        Aplicar(vm, obra);
        if (await db.ProrrogasObra.AnyAsync(p => p.ObraId == obra.Id))
            obra.FechaFinalContrato = finVigente;
        // Entidad completa como Modified (no solo las propiedades cambiadas): replica el
        // guardado anterior de la página — siempre emite el UPDATE, así el RowVersion de
        // la sesión se verifica aunque el usuario guarde sin cambios.
        db.Entry(obra).State = EntityState.Modified;
        db.Entry(obra).Property(o => o.RowVersion).OriginalValue = vm.RowVersion;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Elimina una obra. El chequeo de dependencias (certificados, redeterminaciones,
    /// estructuras, tabla de ponderación, planificación) produce un mensaje específico
    /// antes de chocar con las FK Restrict; el RowVersion de la grilla viaja al DELETE.
    /// Devuelve false si ya la había eliminado otro usuario, para que la página informe.
    /// </summary>
    public async Task<bool> EliminarAsync(int obraId, byte[] rowVersion)
    {
        ExigirRol("eliminar la obra");
        await using var db = await dbFactory.CreateDbContextAsync();

        // Los 5 conteos en un solo round-trip (subconsultas correlacionadas); null si la
        // obra ya no existe.
        var deps = await db.Obras.AsNoTracking()
            .Where(o => o.Id == obraId)
            .Select(o => new
            {
                Certificados = db.Certificados.Count(c => c.ObraId == o.Id),
                Redeterminaciones = db.RedeterminacionesGuardadas.Count(r => r.ObraId == o.Id),
                Estructuras = o.EstructurasCostos.Count,
                Tablas = o.TablasPonderacion.Count,
                Planes = db.Planificaciones.Count(p => p.ObraId == o.Id)
            })
            .FirstOrDefaultAsync();
        if (deps is null) return false; // ya la borró otro usuario
        Validacion.Exigir(ObraValidator.Eliminar(deps.Certificados, deps.Redeterminaciones, deps.Estructuras, deps.Tablas, deps.Planes));

        var obra = await db.Obras.FirstOrDefaultAsync(o => o.Id == obraId);
        if (obra is null) return false; // ya la borró otro usuario

        db.Entry(obra).Property(o => o.RowVersion).OriginalValue = rowVersion;
        db.Obras.Remove(obra);
        await db.SaveChangesAsync();
        return true;
    }

    // ── Prórrogas de plazo ───────────────────────────────────────────────────────

    /// <summary>
    /// Registra una prórroga: correlativo Max+1, parte del fin de contrato vigente y lo
    /// reemplaza por la fecha nueva. Prórroga y fecha de la obra se confirman en un único
    /// SaveChanges (el rowversion de la obra cambia: una edición concurrente de la ficha
    /// termina en conflicto, que es lo que corresponde).
    /// </summary>
    public async Task AgregarProrrogaAsync(NuevaProrrogaVM vm)
    {
        ExigirRol("registrar una prórroga");
        await using var db = await dbFactory.CreateDbContextAsync();

        var obra = await db.Obras.Include(o => o.Prorrogas)
            .FirstOrThrowAsync(o => o.Id == vm.ObraId, "Obra no encontrada.");

        Validacion.Exigir(ObraValidator.AgregarProrroga(obra.FechaFinalContrato, vm.FechaFinNueva));

        obra.Prorrogas.Add(new ProrrogaObra
        {
            Numero = (obra.Prorrogas.Count == 0 ? 0 : obra.Prorrogas.Max(p => p.Numero)) + 1,
            FechaFinAnterior = obra.FechaFinalContrato!.Value,
            FechaFinNueva = vm.FechaFinNueva!.Value.Date,
            FechaActo = vm.FechaActo?.Date,
            IFActo = string.IsNullOrWhiteSpace(vm.IFActo) ? null : vm.IFActo.Trim(),
            Observaciones = string.IsNullOrWhiteSpace(vm.Observaciones) ? null : vm.Observaciones.Trim()
        });
        obra.FechaFinalContrato = vm.FechaFinNueva.Value.Date;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Elimina la última prórroga de la obra y restaura la fecha de fin anterior. false
    /// si ya no existía (patrón "ya eliminado por otro usuario").
    /// </summary>
    public async Task<bool> EliminarProrrogaAsync(int prorrogaId)
    {
        ExigirRol("eliminar una prórroga");
        await using var db = await dbFactory.CreateDbContextAsync();

        var prorroga = await db.ProrrogasObra.Include(p => p.Obra)
            .FirstOrDefaultAsync(p => p.Id == prorrogaId);
        if (prorroga is null) return false;

        var hayPosterior = await db.ProrrogasObra.AnyAsync(p => p.ObraId == prorroga.ObraId && p.Numero > prorroga.Numero);
        Validacion.Exigir(ObraValidator.EliminarProrroga(prorroga.Numero, hayPosterior));

        prorroga.Obra.FechaFinalContrato = prorroga.FechaFinAnterior;
        db.ProrrogasObra.Remove(prorroga);
        await db.SaveChangesAsync();
        return true;
    }

    // ── helpers ──────────────────────────────────────────────────────────────────

    /// <summary>Entidad → VM (todos los campos del formulario/detalle + Id y RowVersion).</summary>
    private static T Mapear<T>(Obra o, T vm) where T : ObraVM
    {
        vm.Id = o.Id;
        vm.Nombre = o.Nombre;
        vm.NumeroLicitacion = o.NumeroLicitacion;
        vm.Antecedentes = o.Antecedentes;
        vm.Contratista = o.Contratista;
        vm.PUByC = o.PUByC;
        vm.FechaOferta = o.FechaOferta;
        vm.OfertaFinal = o.OfertaFinal;
        vm.FechaAdjudicacion = o.FechaAdjudicacion;
        vm.IFAdjudicacion = o.IFAdjudicacion;
        vm.FechaContrato = o.FechaContrato;
        vm.IFContrato = o.IFContrato;
        vm.FechaActaInicio = o.FechaActaInicio;
        vm.IFActaInicio = o.IFActaInicio;
        vm.PlazoObra = o.PlazoObra;
        vm.FechaFinalContrato = o.FechaFinalContrato; // vigente (ver Obra.FechaFinalContrato)
        vm.DirectorObra = o.DirectorObra;
        vm.PresupuestoOficial = o.PresupuestoOficial;
        vm.PresupuestoOficialUSD = o.PresupuestoOficialUSD;
        vm.PresupuestoOficialEUR = o.PresupuestoOficialEUR;
        vm.PresupuestoAdjudicado = o.PresupuestoAdjudicado;
        vm.PresupuestoAdjudicadoUSD = o.PresupuestoAdjudicadoUSD;
        vm.PresupuestoAdjudicadoEUR = o.PresupuestoAdjudicadoEUR;
        vm.DirectorUsuarioId = o.DirectorUsuarioId;
        vm.DirectorNombre = o.DirectorNombre;
        vm.IFDesignacion = o.IFDesignacion;
        vm.RowVersion = o.RowVersion;
        return vm;
    }

    /// <summary>
    /// VM → entidad: solo los campos que el formulario edita. CorreoDirectorObra y los
    /// campos de auditoría no se tocan (en edición conservan el valor cargado de la DB).
    /// </summary>
    private static void Aplicar(ObraVM vm, Obra o)
    {
        o.Nombre = vm.Nombre;
        o.NumeroLicitacion = vm.NumeroLicitacion;
        o.Antecedentes = vm.Antecedentes;
        o.Contratista = vm.Contratista;
        o.PUByC = vm.PUByC;
        o.FechaOferta = vm.FechaOferta;
        o.OfertaFinal = vm.OfertaFinal;
        o.FechaAdjudicacion = vm.FechaAdjudicacion;
        o.IFAdjudicacion = vm.IFAdjudicacion;
        o.FechaContrato = vm.FechaContrato;
        o.IFContrato = vm.IFContrato;
        o.FechaActaInicio = vm.FechaActaInicio;
        o.IFActaInicio = vm.IFActaInicio;
        o.PlazoObra = vm.PlazoObra;
        o.FechaFinalContrato = vm.FechaFinalContrato;
        o.DirectorObra = vm.DirectorObra;
        o.PresupuestoOficial = vm.PresupuestoOficial;
        o.PresupuestoOficialUSD = vm.PresupuestoOficialUSD;
        o.PresupuestoOficialEUR = vm.PresupuestoOficialEUR;
        o.PresupuestoAdjudicado = vm.PresupuestoAdjudicado;
        o.PresupuestoAdjudicadoUSD = vm.PresupuestoAdjudicadoUSD;
        o.PresupuestoAdjudicadoEUR = vm.PresupuestoAdjudicadoEUR;
        o.DirectorUsuarioId = vm.DirectorUsuarioId;
        o.IFDesignacion = vm.IFDesignacion;
    }
}
