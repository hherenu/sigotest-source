using SIGO.Models.Enums;

namespace SIGO.Models.ViewModels;

/// <summary>
/// Usuario aplanado para la grilla y el formulario de /usuarios (sin entidades EF:
/// la UI no puede editar por accidente algo trackeado ni acoplarse al esquema).
/// Id == 0 significa alta; con Id la edición usa el RowVersion como token.
/// </summary>
public class UsuarioVM
{
    public int Id { get; set; }

    /// <summary>Identidad Windows "DOMINIO\usuario". Única; solo se define en el alta.</summary>
    public string? WindowsUser { get; set; }

    public string? Nombre { get; set; }

    /// <summary>Correo para notificaciones. En blanco se persiste null.</summary>
    public string? Email { get; set; }

    /// <summary>Inactivo = conserva historial/auditoría pero pierde todo acceso por rol.</summary>
    public bool Activo { get; set; } = true;

    // IEnumerable (no List): el Value de RadzenCheckBoxList bindea IEnumerable<T>.
    public IEnumerable<RolUsuario> Roles { get; set; } = new List<RolUsuario>();

    /// <summary>
    /// Token de concurrencia de la fila tal como se listó: viaja al UPDATE/DELETE para
    /// detectar ediciones concurrentes de otro administrador.
    /// </summary>
    public byte[] RowVersion { get; set; } = [];
}
