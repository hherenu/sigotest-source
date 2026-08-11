using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SIGO.Models.ViewModels;

namespace SIGO.Data;

/// <summary>
/// "No encontrado" como tipo propio: hereda de InvalidOperationException para que
/// Persistencia lo notifique igual que cualquier regla de negocio, pero permite a
/// controllers y handlers mapearlo a 404 sin adivinar por el texto del mensaje.
/// </summary>
public class EntidadNoEncontradaException(string mensaje) : InvalidOperationException(mensaje);

public static class QueryExtensions
{
    /// <summary>
    /// FirstOrDefaultAsync que lanza <see cref="EntidadNoEncontradaException"/> con el
    /// mensaje dado. Unifica el patrón "?? throw new InvalidOperationException(...)"
    /// que estaba repetido en todos los servicios.
    /// </summary>
    public static async Task<T> FirstOrThrowAsync<T>(this IQueryable<T> query,
        Expression<Func<T, bool>> predicado, string mensajeNoEncontrado) where T : class =>
        await query.FirstOrDefaultAsync(predicado)
        ?? throw new EntidadNoEncontradaException(mensajeNoEncontrado);

    /// <summary>
    /// Proyección mínima de la obra para cabeceras (Id, nombre, N° de licitación), o
    /// null si no existe. ÚNICA implementación: los servicios que solo necesitan estos
    /// campos la usan en vez de cargar la entidad completa.
    /// </summary>
    public static Task<ObraResumenVM?> ResumenObraAsync(this AppDbContext db, int obraId) =>
        db.Obras.AsNoTracking()
            .Where(o => o.Id == obraId)
            .Select(o => new ObraResumenVM(o.Id, o.Nombre, o.NumeroLicitacion))
            .FirstOrDefaultAsync();
}
