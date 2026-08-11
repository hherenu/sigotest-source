using Microsoft.EntityFrameworkCore;
using SIGO.Data;
using SIGO.Models;
using SIGO.Models.Enums;
using SIGO.Models.ViewModels;
using SIGO.Services.Validaciones;

namespace SIGO.Services;

/// <summary>
/// Ediciones y bajas de usuarios (roles de aplicación). No hay alta manual: los
/// usuarios se registran solos en su primer ingreso (RolesClaimsTransformation).
/// La tabla Usuarios alimenta esa transformación en cada request, sin caché a
/// propósito — los cambios de rol aplican cuando el usuario recarga la página (F5).
/// </summary>
public class UsuarioService(IDbContextFactory<AppDbContext> dbFactory, ICurrentUser currentUser)
{
    /// <summary>Defensa en profundidad (ver <see cref="Roles.Exigir"/>): toda mutación exige Admin, igual que el [Authorize] de /usuarios.</summary>
    private void ExigirRol(string accion) => Roles.Exigir(currentUser.IsInRole, Roles.Admin, accion);

    /// <summary>Todos los usuarios con sus roles, ordenados por nombre (grilla de /usuarios).</summary>
    public async Task<List<UsuarioVM>> ListarAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        // Solo lectura: proyección directa a VM en SQL, la UI nunca recibe entidades.
        return await db.Usuarios.AsNoTracking()
            .OrderBy(u => u.Nombre)
            .Select(u => new UsuarioVM
            {
                Id = u.Id,
                WindowsUser = u.WindowsUser,
                Nombre = u.Nombre,
                Email = u.Email,
                Activo = u.Activo,
                Roles = u.Roles.Select(r => r.Rol).ToList(),
                RowVersion = u.RowVersion
            })
            .ToListAsync();
    }

    /// <summary>
    /// Edición de un usuario (no hay alta manual: el registro es automático al primer
    /// ingreso). El RowVersion del VM es el token de la fila listada y viaja al UPDATE
    /// (concurrencia entre admins); la identidad Windows nunca se modifica.
    /// </summary>
    public async Task ActualizarAsync(UsuarioVM vm)
    {
        ExigirRol("actualizar el usuario");
        Validacion.Exigir(UsuarioValidator.Actualizar(vm.Nombre));

        await using var db = await dbFactory.CreateDbContextAsync();
        var roles = vm.Roles.ToList();

        var fresco = await db.Usuarios.Include(u => u.Roles)
            .FirstOrThrowAsync(u => u.Id == vm.Id, "El usuario ya no existe (lo eliminó otro administrador).");

        // La app nunca puede quedar sin un Admin activo: si esta edición le saca
        // Admin (o lo desactiva) al último, se rechaza. La query del "otro admin"
        // solo corre cuando la edición efectivamente lo deja sin Admin activo.
        // El TOCTOU chequeo→save es el conocido (BAJO, auditoría 2026-07-22).
        var esAdminActivo = fresco.Activo && fresco.Roles.Any(r => r.Rol == RolUsuario.Admin);
        var seguiraComoAdminActivo = vm.Activo && roles.Contains(RolUsuario.Admin);
        var hayOtroAdminActivo = !(esAdminActivo && !seguiraComoAdminActivo)
                                 || await HayOtroAdminActivoAsync(db, fresco.Id);
        Validacion.Exigir(UsuarioValidator.QuitarAdmin(esAdminActivo, seguiraComoAdminActivo, hayOtroAdminActivo));

        // El token de la fila listada viaja al UPDATE (concurrencia entre admins).
        // AplicarTokenSesion (no OriginalValue pelado): tocar Activo fuerza el UPDATE
        // del padre aunque la edición solo cambie filas de UsuariosRoles — sin eso el
        // token nunca se comparaba en cambios solo-de-roles y dos admins concurrentes
        // podían dejar a un usuario sin ningún rol sin aviso.
        db.AplicarTokenSesion(fresco, vm.RowVersion, u => u.Activo);
        fresco.Nombre = vm.Nombre!.Trim();
        // El Email NO se edita acá: viene de AD en el auto-registro (RolesClaimsTransformation).
        fresco.Activo = vm.Activo;
        db.UsuariosRoles.RemoveRange(fresco.Roles.Where(r => !roles.Contains(r.Rol)));
        foreach (var r in roles.Where(r => fresco.Roles.All(x => x.Rol != r)))
            db.UsuariosRoles.Add(new UsuarioRol { UsuarioId = fresco.Id, Rol = r });

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Elimina un usuario; false si ya lo había borrado otro administrador (mismo patrón
    /// que RedeterminacionService.EliminarDisparoAsync). Se rechaza si es director de
    /// obras o si es el último Admin activo. El token de la fila listada viaja al DELETE.
    /// </summary>
    public async Task<bool> EliminarAsync(int usuarioId, byte[] rowVersion)
    {
        ExigirRol("eliminar el usuario");
        await using var db = await dbFactory.CreateDbContextAsync();

        var obras = await db.Obras.CountAsync(o => o.DirectorUsuarioId == usuarioId);
        Validacion.Exigir(UsuarioValidator.EliminarDirector(obras));

        var fresco = await db.Usuarios.Include(x => x.Roles).FirstOrDefaultAsync(x => x.Id == usuarioId);
        if (fresco is null) return false; // ya lo borró otro administrador

        // Mismo invariante y mismo TOCTOU conocido que en ActualizarAsync.
        var esAdminActivo = fresco.Activo && fresco.Roles.Any(r => r.Rol == RolUsuario.Admin);
        var hayOtroAdminActivo = !esAdminActivo || await HayOtroAdminActivoAsync(db, fresco.Id);
        Validacion.Exigir(UsuarioValidator.EliminarAdmin(esAdminActivo, hayOtroAdminActivo));

        db.Entry(fresco).Property(x => x.RowVersion).OriginalValue = rowVersion;
        db.Usuarios.Remove(fresco);
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>¿Existe OTRO usuario activo con rol Admin además del indicado?</summary>
    private static Task<bool> HayOtroAdminActivoAsync(AppDbContext db, int exceptoUsuarioId) =>
        db.UsuariosRoles.AnyAsync(r =>
            r.Rol == RolUsuario.Admin && r.UsuarioId != exceptoUsuarioId && r.Usuario.Activo);
}
