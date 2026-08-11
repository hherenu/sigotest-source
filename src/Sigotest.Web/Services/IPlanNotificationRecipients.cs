using Microsoft.EntityFrameworkCore;
using SIGO.Data;
using SIGO.Models.Enums;

namespace SIGO.Services;

/// <summary>
/// Destinatarios del circuito de Planificación, resueltos desde la tabla Usuarios
/// (fuente de verdad de roles y correos; se administra en /usuarios).
/// </summary>
public interface IPlanNotificationRecipients
{
    /// <summary>Correos de los usuarios activos con el rol dado (con Email cargado).</summary>
    Task<IReadOnlyCollection<string>> ConRolAsync(RolUsuario rol);

    /// <summary>
    /// Correo(s) del director de la obra: el usuario asignado (DirectorUsuario) si está
    /// activo y tiene mail; si no, el texto libre interino CorreoDirectorObra.
    /// </summary>
    Task<IReadOnlyCollection<string>> DirectorDeObraAsync(int obraId);
}

public class UsuariosNotificationRecipients(IDbContextFactory<AppDbContext> dbFactory) : IPlanNotificationRecipients
{
    /// <summary>
    /// Criterio ÚNICO de resolución del correo del director: el mail del usuario
    /// asignado activo si existe; si no, el texto libre interino de la obra separado
    /// por comas. Lo usan DirectorDeObraAsync y el digest mensual — antes eran dos
    /// implementaciones que podían divergir.
    /// </summary>
    public static string[] CorreosDirector(string? emailUsuarioActivo, string? correoDirectorObra) =>
        !string.IsNullOrWhiteSpace(emailUsuarioActivo)
            ? [emailUsuarioActivo]
            : (correoDirectorObra ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public async Task<IReadOnlyCollection<string>> ConRolAsync(RolUsuario rol)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Usuarios.AsNoTracking()
            .Where(u => u.Activo && u.Email != null && u.Email != ""
                        && u.Roles.Any(r => r.Rol == rol))
            .Select(u => u.Email!)
            .ToListAsync();
    }

    public async Task<IReadOnlyCollection<string>> DirectorDeObraAsync(int obraId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var obra = await db.Obras.AsNoTracking()
            .Where(o => o.Id == obraId)
            .Select(o => new
            {
                EmailDirector = o.DirectorUsuario != null && o.DirectorUsuario.Activo
                    ? o.DirectorUsuario.Email : null,
                o.CorreoDirectorObra
            })
            .FirstOrDefaultAsync();

        return obra is null ? [] : CorreosDirector(obra.EmailDirector, obra.CorreoDirectorObra);
    }
}
