using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SIGO.Models;

namespace SIGO.Data;

public static class ConcurrenciaExtensions
{
    /// <summary>
    /// Patrón compartido de las grillas de edición (certificados y planificación):
    /// 1) Con el token de la sesión de edición como RowVersion original, dos usuarios
    ///    con la misma grilla abierta no se pisan en silencio — el segundo guardado
    ///    recibe el conflicto de concurrencia (sin token solo se detectaban carreras
    ///    en vuelo, no el lost update entre sesiones).
    /// 2) Forzar una propiedad del padre como Modified hace que el UPDATE con
    ///    rowversion exista aunque solo cambien filas hijas: serializa guardar la
    ///    grilla contra las transiciones de estado (cerrar, marcar cargada).
    /// </summary>
    public static void AplicarTokenSesion<TEntity>(
        this DbContext db, TEntity entidad, byte[]? rowVersionSesion,
        Expression<Func<TEntity, object?>> propiedadATocar) where TEntity : BaseEntity
    {
        var entry = db.Entry(entidad);
        if (rowVersionSesion is not null)
            entry.Property(e => e.RowVersion).OriginalValue = rowVersionSesion;
        entry.Property(propiedadATocar).IsModified = true;
    }
}
