using System.DirectoryServices.AccountManagement;
using System.Runtime.Versioning;

namespace SIGO.Services;

/// <summary>
/// Email institucional de una identidad Windows, resuelto desde Active Directory
/// (atributo mail de la cuenta). El Email de la tabla Usuarios NO se tipea a mano:
/// se toma de acá en el auto-registro (RolesClaimsTransformation); /usuarios lo muestra solo-lectura.
/// </summary>
public interface IDirectoryEmailResolver
{
    /// <summary>
    /// Email de la cuenta en AD, o null si la cuenta no tiene mail cargado o el
    /// dominio no respondió (el fallo se loguea; los llamadores tratan null como
    /// "sin email", igual que un usuario sin mail en /usuarios).
    /// </summary>
    string? EmailDe(string windowsUser);
}

[SupportedOSPlatform("windows")]
public class ActiveDirectoryEmailResolver(ILogger<ActiveDirectoryEmailResolver> logger) : IDirectoryEmailResolver
{
    public string? EmailDe(string windowsUser)
    {
        // "SBASE\ncarracedo" → sAMAccountName "ncarracedo".
        var cuenta = CuentaWindows.SinDominio(windowsUser);
        try
        {
            using var dominio = new PrincipalContext(ContextType.Domain);
            using var usuario = UserPrincipal.FindByIdentity(dominio, IdentityType.SamAccountName, cuenta);
            var email = usuario?.EmailAddress;
            return string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        }
        catch (Exception ex)
        {
            // Máquina fuera de dominio o DC caído: el registro/refresh sigue sin email.
            logger.LogWarning(ex, "No se pudo resolver el email de {Usuario} en Active Directory.", windowsUser);
            return null;
        }
    }
}
