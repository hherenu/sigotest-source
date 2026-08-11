using SIGO.Models.Enums;

namespace SIGO.Models;

/// <summary>
/// Usuario de la aplicación. La autenticación es Windows (Negotiate); esta tabla solo
/// aporta la autorización (roles) y los datos de contacto para notificaciones.
/// </summary>
public class Usuario : BaseEntity
{
    /// <summary>Identidad Windows tal como llega del token: "DOMINIO\usuario". Única.</summary>
    public string WindowsUser { get; set; } = string.Empty;

    public string Nombre { get; set; } = string.Empty;

    /// <summary>Correo para las notificaciones (mails de planificación).</summary>
    public string? Email { get; set; }

    /// <summary>Inactivo = conserva historial/auditoría pero pierde todo acceso por rol.</summary>
    public bool Activo { get; set; } = true;

    public ICollection<UsuarioRol> Roles { get; set; } = [];
}

/// <summary>Rol asignado a un usuario (un usuario puede tener varios).</summary>
public class UsuarioRol
{
    public int Id { get; set; }
    public int UsuarioId { get; set; }
    public Usuario Usuario { get; set; } = null!;
    public RolUsuario Rol { get; set; }

    /// <summary>Token de concurrencia (rowversion de SQL Server).</summary>
    [System.ComponentModel.DataAnnotations.Timestamp]
    public byte[] RowVersion { get; set; } = [];
}
