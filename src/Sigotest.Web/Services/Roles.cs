namespace SIGO.Services;

/// <summary>
/// Nombres de rol como constantes para los atributos [Authorize(Roles=...)] y los
/// AuthorizeView (los atributos exigen constantes; el enum RolUsuario es la fuente
/// de verdad persistida y estos strings deben coincidir con sus nombres).
/// </summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string Director = "Director";
    public const string Gerente = "Gerente";
    public const string Presupuesto = "Presupuesto";
    public const string CCyR = "CCyR";

    /// <summary>Módulo de obras, certificaciones y redeterminaciones.</summary>
    public const string Certificaciones = $"{Admin},{CCyR}";
    /// <summary>Módulo de planificación (los tres actores del circuito).</summary>
    public const string Planificacion = $"{Admin},{Director},{Gerente},{Presupuesto}";
    /// <summary>
    /// Pueden editar la grilla del plan (bloques, montos, obra finalizada). Gerente y
    /// Presupuesto son solo lectura: el gerente aprueba o envía a revisión, no edita.
    /// </summary>
    public const string PlanificacionEdicion = $"{Admin},{Director}";
    /// <summary>Pueden marcar el plan como Cargada (el director es quien carga).</summary>
    public const string PlanificacionCarga = $"{Admin},{Director}";
    /// <summary>Pueden aprobar o enviar a revisión (el gerente controla).</summary>
    public const string PlanificacionAprobacion = $"{Admin},{Gerente}";
    /// <summary>
    /// Pueden tomar conocimiento de un plan Aprobado (genera el snapshot del mes).
    /// Es la única escritura de Presupuesto, que fuera de esto sigue siendo solo lectura.
    /// </summary>
    public const string PlanificacionConocimiento = $"{Admin},{Presupuesto}";

    /// <summary>
    /// true si el usuario es Director SIN ningún rol de alcance total (Admin/Gerente/
    /// Presupuesto): en ese caso solo ve y opera las obras que tiene asignadas como
    /// director. Sirve tanto para ClaimsPrincipal.IsInRole como para ICurrentUser.IsInRole.
    /// </summary>
    public static bool SoloDirector(Func<string, bool> isInRole) =>
        isInRole(Director) && !isInRole(Admin) && !isInRole(Gerente) && !isInRole(Presupuesto);

    /// <summary>
    /// true si el usuario tiene alguno de los roles de la lista separada por comas
    /// (las constantes compuestas de esta clase). Sirve para ClaimsPrincipal.IsInRole
    /// e ICurrentUser.IsInRole; lo usan los ExigirRol de los servicios y las vistas.
    /// </summary>
    public static bool TieneAlguno(Func<string, bool> isInRole, string rolesPermitidos) =>
        rolesPermitidos.Split(',').Any(isInRole);

    /// <summary>
    /// Defensa en profundidad detrás del gating de la UI, ÚNICA implementación para
    /// los ExigirRol de todos los servicios: toda mutación exige un rol del módulo,
    /// no solo el [Authorize] de las páginas — un servicio invocado desde otro lado
    /// no puede saltarse el control. InvalidOperationException a propósito:
    /// Persistencia la traduce en una notificación al usuario.
    /// </summary>
    public static void Exigir(Func<string, bool> isInRole, string rolesPermitidos, string accion)
    {
        if (!TieneAlguno(isInRole, rolesPermitidos))
            throw new InvalidOperationException($"Tu usuario no tiene permiso para {accion}.");
    }
}
