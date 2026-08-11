using Microsoft.EntityFrameworkCore;
using SIGO.Data;
using SIGO.Models;
using SIGO.Models.ViewModels;
using SIGO.Services.Validaciones;

namespace SIGO.Services;

/// <summary>
/// Tabla de ponderación de una obra y sus insumos (base del cálculo de
/// redeterminaciones). Persistencia movida desde PonderacionEditar.razor: la página
/// solo bindea VMs y notifica; las reglas puras viven en <see cref="PonderacionValidator"/>.
/// </summary>
public class PonderacionService(IDbContextFactory<AppDbContext> dbFactory, ICurrentUser currentUser)
{
    /// <summary>Defensa en profundidad (ver <see cref="Roles.Exigir"/>): módulo Certificaciones.</summary>
    private void ExigirRol(string accion) => Roles.Exigir(currentUser.IsInRole, Roles.Certificaciones, accion);

    /// <summary>
    /// Estado completo de la página: obra + tabla (si existe) + insumos ordenados por
    /// número. Null si la obra no existe.
    /// </summary>
    public async Task<PonderacionVM?> BuildVmAsync(int obraId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var obra = await db.ResumenObraAsync(obraId);
        if (obra is null) return null;

        var vm = new PonderacionVM
        {
            ObraId = obraId,
            ObraNombre = obra.Nombre,
            NumeroLicitacion = obra.NumeroLicitacion
        };

        var tabla = await db.TablasPonderacion.AsNoTracking()
            .Include(t => t.Items.OrderBy(i => i.Numero))
                .ThenInclude(i => i.Indice)
            .FirstOrDefaultAsync(t => t.ObraId == obraId);
        if (tabla is null) return vm;

        vm.TablaId = tabla.Id;
        vm.TablaNombre = tabla.Nombre;
        vm.Items = tabla.Items.Select(i => new ItemPonderacionVM
        {
            Id = i.Id,
            Numero = i.Numero,
            Insumo = i.Insumo,
            PesoPorcentaje = i.PesoPorcentaje,
            IndiceId = i.IndiceId,
            IndiceNombre = i.Indice.FamiliaRecurso,
            DescripcionINDEC = i.DescripcionINDEC,
            RowVersion = i.RowVersion
        }).ToList();
        return vm;
    }

    /// <summary>Catálogo de índices INDEC para el dropdown del form (ordenado por familia).</summary>
    public async Task<List<IndiceOpcionVM>> GetIndicesAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        return await db.IndicesINDEC.AsNoTracking()
            .OrderBy(i => i.FamiliaRecurso)
            .Select(i => new IndiceOpcionVM
            {
                Id = i.Id,
                FamiliaRecurso = i.FamiliaRecurso,
                CodigoIndice = i.CodigoIndice
            })
            .ToListAsync();
    }

    /// <summary>
    /// Crea la tabla de ponderación de la obra (nombre por defecto "Tabla Principal").
    /// El índice único sobre ObraId garantiza una sola tabla por obra: si otro usuario
    /// la creó en paralelo, el guardado falla (duplicado) y la página recarga la existente.
    /// </summary>
    public async Task CrearTablaAsync(int obraId, string? nombre)
    {
        ExigirRol("crear la tabla de ponderación");
        await using var db = await dbFactory.CreateDbContextAsync();

        db.TablasPonderacion.Add(new TablaPonderacion
        {
            ObraId = obraId,
            Nombre = string.IsNullOrWhiteSpace(nombre) ? "Tabla Principal" : nombre.Trim()
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Agrega un insumo a la tabla. El Numero sale de la DB (Max+1), no de la colección
    /// en memoria: otro usuario pudo haber agregado insumos desde que se cargó la página.
    /// </summary>
    public async Task AgregarItemAsync(int tablaPonderacionId, ItemPonderacionFormVM form)
    {
        ExigirRol("agregar el insumo");
        Validacion.Exigir(PonderacionValidator.GuardarItem(form.Insumo, form.Peso, form.IndiceId));

        await using var db = await dbFactory.CreateDbContextAsync();

        var maxNumero = await db.ItemsPonderacion
            .Where(i => i.TablaPonderacionId == tablaPonderacionId)
            .MaxAsync(i => (int?)i.Numero) ?? 0;

        db.ItemsPonderacion.Add(new ItemPonderacion
        {
            TablaPonderacionId = tablaPonderacionId,
            Numero = maxNumero + 1,
            Insumo = form.Insumo!.Trim(),
            PesoPorcentaje = form.Peso!.Value,
            IndiceId = form.IndiceId!.Value,
            DescripcionINDEC = string.IsNullOrWhiteSpace(form.DescripcionINDEC) ? null : form.DescripcionINDEC.Trim()
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Edita un insumo existente. El RowVersion de la sesión viaja como token: si otro
    /// usuario lo modificó desde que la página lo mostró, salta el conflicto de
    /// concurrencia en vez de un lost update.
    /// </summary>
    public async Task EditarItemAsync(int itemPonderacionId, ItemPonderacionFormVM form, byte[]? rowVersionSesion = null)
    {
        ExigirRol("modificar el insumo");
        Validacion.Exigir(PonderacionValidator.GuardarItem(form.Insumo, form.Peso, form.IndiceId));

        await using var db = await dbFactory.CreateDbContextAsync();

        var item = await db.ItemsPonderacion.FirstOrThrowAsync(i => i.Id == itemPonderacionId,
            "El insumo a editar ya no existe (otro usuario pudo haberlo eliminado).");

        if (rowVersionSesion is not null)
            db.Entry(item).Property(i => i.RowVersion).OriginalValue = rowVersionSesion;

        item.Insumo = form.Insumo!.Trim();
        item.PesoPorcentaje = form.Peso!.Value;
        item.IndiceId = form.IndiceId!.Value;
        item.DescripcionINDEC = string.IsNullOrWhiteSpace(form.DescripcionINDEC) ? null : form.DescripcionINDEC.Trim();

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Elimina un insumo. Devuelve false si ya no existía —eliminado por otro usuario—
    /// para que la página informe en vez de fallar (patrón EliminarDisparoAsync). La
    /// regla de FK Restrict (insumo referenciado por redeterminaciones guardadas) lanza
    /// con mensaje específico antes del error de FK.
    /// </summary>
    public async Task<bool> EliminarItemAsync(int itemPonderacionId, byte[] rowVersionSesion)
    {
        ExigirRol("eliminar el insumo");
        await using var db = await dbFactory.CreateDbContextAsync();

        var item = await db.ItemsPonderacion.FirstOrDefaultAsync(i => i.Id == itemPonderacionId);
        if (item is null) return false;

        var usos = await db.RedeterminacionesGuardadasItems
            .CountAsync(x => x.ItemPonderacionId == item.Id);
        Validacion.Exigir(PonderacionValidator.EliminarItem(item.Insumo, usos));

        // El token de la sesión viaja al DELETE (detecta ediciones concurrentes desde
        // que la página mostró el insumo).
        db.Entry(item).Property(i => i.RowVersion).OriginalValue = rowVersionSesion;

        db.ItemsPonderacion.Remove(item);
        await db.SaveChangesAsync();
        return true;
    }
}
