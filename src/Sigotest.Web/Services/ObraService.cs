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
        return obra is null ? null : Mapear(obra, new ObraVM());
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
            .Include(o => o.DirectorUsuario)
            .FirstOrDefaultAsync(o => o.Id == id);
        if (obra is null) return null;

        var vm = Mapear(obra, new ObraDetalleVM());
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
        Validacion.Exigir(ObraValidator.Guardar(vm.Nombre, vm.NumeroLicitacion));

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
        Validacion.Exigir(ObraValidator.Guardar(vm.Nombre, vm.NumeroLicitacion));

        await using var db = await dbFactory.CreateDbContextAsync();
        var obra = await db.Obras.FirstOrDefaultAsync(o => o.Id == vm.Id)
            // La fila ya no existe: mismo desenlace que producía el UPDATE de la página
            // (0 filas afectadas) — Persistencia lo notifica como conflicto de concurrencia
            // ("recargá la página"), que es exactamente lo que corresponde hacer.
            ?? throw new DbUpdateConcurrencyException(
                "La obra fue eliminada por otro usuario mientras la editabas.");

        Aplicar(vm, obra);
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
        vm.FechaFinalContrato = o.FechaFinalContrato;
        vm.DirectorObra = o.DirectorObra;
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
        o.DirectorUsuarioId = vm.DirectorUsuarioId;
        o.IFDesignacion = vm.IFDesignacion;
    }
}
