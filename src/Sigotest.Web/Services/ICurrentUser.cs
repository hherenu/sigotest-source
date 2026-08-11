using Microsoft.AspNetCore.Components.Authorization;

namespace SIGO.Services;

/// <summary>
/// Abstracción del usuario actual para auditoría. Con autenticación Windows devuelve
/// "DOMINIO\usuario"; null si el request no está autenticado.
/// </summary>
public interface ICurrentUser
{
    string? UserId { get; }

    /// <summary>true si el usuario actual tiene el rol de aplicación indicado.</summary>
    bool IsInRole(string rol);
}

public class CurrentUser(
    IHttpContextAccessor httpContextAccessor,
    AuthenticationStateProvider authStateProvider) : ICurrentUser
{
    public bool IsInRole(string rol) => Principal?.IsInRole(rol) == true;

    public string? UserId => Principal?.Identity?.Name;

    private System.Security.Claims.ClaimsPrincipal? Principal
    {
        get
        {
            // Controllers y render inicial: la identidad viene del request HTTP.
            // Solo se confía en identidades autenticadas (un claim suelto no alcanza).
            var user = httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated == true)
                return user;

            // Circuito Blazor interactivo: HttpContext no está garantizado; el estado
            // de autenticación del circuito ya quedó fijado al abrir la conexión.
            try
            {
                var task = authStateProvider.GetAuthenticationStateAsync();
                if (task is { IsCompletedSuccessfully: true })
                {
                    var principal = task.Result.User;
                    if (principal.Identity?.IsAuthenticated == true)
                        return principal;
                }
            }
            catch (InvalidOperationException)
            {
                // Scope sin estado de circuito (ej. request de controller): ya se
                // intentó HttpContext arriba.
            }

            return null;
        }
    }
}
