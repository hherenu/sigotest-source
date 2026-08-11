namespace SIGO.Services.Validaciones;

/// <summary>
/// Reglas de usuarios, puras y sin acceso a datos: el servicio les pasa lo que ya leyó
/// y lanza si devuelven error. Invariante central: la app NUNCA puede quedar sin un
/// Admin activo — los roles se inyectan como claims en cada request desde la tabla
/// Usuarios, así que sin un Admin activo nadie podría volver a entrar a /usuarios a
/// arreglarlo (solo quedaría el bootstrap de Seguridad:AdminsIniciales).
/// </summary>
public static class UsuarioValidator
{
    /// <summary>
    /// Campos mínimos de la edición (no hay alta manual: el registro es automático al
    /// primer ingreso y la identidad Windows nunca se edita). Defensa en profundidad
    /// detrás del RequiredValidator del formulario (mismo mensaje), que además deja
    /// pasar un texto de solo espacios: sin esta regla el Trim() lo persistía como
    /// cadena vacía y con la propiedad en null era un NullReferenceException que
    /// Persistencia no traduce.
    /// </summary>
    public static string? Actualizar(string? nombre) =>
        string.IsNullOrWhiteSpace(nombre) ? "El nombre es obligatorio" : null;

    /// <summary>
    /// Edición: si el usuario es hoy un Admin activo y la edición lo deja sin serlo
    /// (le quita el rol Admin o lo desactiva), tiene que existir otro Admin activo.
    /// </summary>
    public static string? QuitarAdmin(bool esAdminActivo, bool seguiraComoAdminActivo, bool hayOtroAdminActivo) =>
        esAdminActivo && !seguiraComoAdminActivo && !hayOtroAdminActivo
            ? "Es el único Admin activo: asigná Admin a otro usuario antes de quitárselo."
            : null;

    /// <summary>
    /// Eliminación: mismo invariante que <see cref="QuitarAdmin"/> — eliminar al último
    /// Admin activo también deja la app sin administración.
    /// </summary>
    public static string? EliminarAdmin(bool esAdminActivo, bool hayOtroAdminActivo) =>
        esAdminActivo && !hayOtroAdminActivo
            ? "Es el único Admin activo: asigná Admin a otro usuario antes de eliminarlo."
            : null;

    /// <summary>
    /// Un usuario que es director de obras no se elimina: sus obras quedarían sin
    /// responsable en el circuito de planificación. Hay que reasignarlas antes, o
    /// desactivar al usuario (que conserva historial y pierde el acceso).
    /// </summary>
    public static string? EliminarDirector(int obrasComoDirector) =>
        obrasComoDirector > 0
            ? $"Es director de {obrasComoDirector} obra(s): reasignalas antes de eliminarlo, o desactivalo."
            : null;
}
