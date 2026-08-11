using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using SIGO.Data;
using SIGO.Models;
using SIGO.Models.Enums;

namespace SIGO.Services;

/// <summary>
/// Agrega al principal autenticado por Windows los roles de aplicación cargados en la
/// tabla Usuarios. No hay alta manual de usuarios: cada identidad se registra sola en
/// su primer ingreso. Bootstrap: el primer ingresante con la tabla vacía queda
/// registrado como Admin (y Seguridad:AdminsIniciales queda como recuperación).
/// </summary>
public class RolesClaimsTransformation(
    IDbContextFactory<AppDbContext> dbFactory,
    IConfiguration config,
    IHostEnvironment env,
    IDirectoryEmailResolver directoryEmails,
    ILogger<RolesClaimsTransformation> logger) : IClaimsTransformation
{
    // Marca de idempotencia: TransformAsync puede ejecutarse más de una vez por request.
    private const string Marker = "AppRoles";

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var name = principal.Identity?.Name;
        if (principal.Identity?.IsAuthenticated != true || string.IsNullOrEmpty(name))
            return principal;
        if (principal.Identities.Any(i => i.Label == Marker))
            return principal;

        var roles = new List<string>();
        await using var db = await dbFactory.CreateDbContextAsync();

        // Proyección y no la entidad: esto corre en cada request y solo necesita
        // Activo + roles.
        var usuario = await db.Usuarios.AsNoTracking()
            .Where(u => u.WindowsUser == name)
            .Select(u => new { u.Activo, Roles = u.Roles.Select(r => r.Rol).ToList() })
            .FirstOrDefaultAsync();
        if (usuario is { Activo: true })
            roles.AddRange(usuario.Roles.Select(r => r.ToString()));

        // Auto-registro (única vía de alta): el primer ingreso crea la fila SIN roles
        // (no habilita nada); el Admin después solo tilda roles en /usuarios. Con la
        // tabla vacía (primer arranque) el ingresante queda como Admin persistido:
        // materializa el bootstrap en vez de dejar a todo el dominio como Admin.
        var esBootstrap = false;
        if (usuario is null)
        {
            // El email viene de AD, no se tipea (una dirección mal cargada rompía las
            // notificaciones). Se resuelve ANTES de la transacción: una consulta al
            // dominio no puede correr con el rango de Usuarios bloqueado en Serializable.
            var emailAd = directoryEmails.EmailDe(name);
            try
            {
                // Serializable: el chequeo de "tabla vacía" y el insert quedan en el
                // mismo rango bloqueado — dos identidades DISTINTAS entrando a la vez
                // en el primer arranque ya no pueden leer ambas la tabla vacía y quedar
                // las dos como Admin persistido.
                await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
                var esElPrimero = !await db.Usuarios.AnyAsync();
                var nuevo = new Usuario { WindowsUser = name, Nombre = CuentaWindows.SinDominio(name), Email = emailAd };
                if (esElPrimero)
                    nuevo.Roles = [new UsuarioRol { Rol = RolUsuario.Admin }];
                db.Usuarios.Add(nuevo);
                await db.SaveChangesAsync();
                await tx.CommitAsync();
                esBootstrap = esElPrimero;
                if (esBootstrap)
                    logger.LogWarning("Bootstrap: {Usuario} quedó registrado como Admin inicial (tabla Usuarios vacía).", name);
            }
            catch (DbUpdateException)
            {
                // Dos requests simultáneos del mismo usuario nuevo: el índice único
                // frena al segundo; la fila ya existe, que es lo que importa.
            }
        }

        if (!roles.Contains(Roles.Admin))
        {
            var iniciales = config.GetSection("Seguridad:AdminsIniciales").Get<string[]>() ?? [];
            // esBootstrap (no "tabla vacía" releído): el claim transitorio del primer
            // arranque es solo para el request que efectivamente persistió el Admin.
            if (iniciales.Contains(name, StringComparer.OrdinalIgnoreCase) || esBootstrap)
                roles.Add(Roles.Admin);
        }

        // Simulador SOLO-DESARROLLO: si Seguridad:SimularRoles tiene valores, REEMPLAZA
        // los roles reales para probar el gating de cada rol sin otras cuentas del
        // dominio. Se edita appsettings.Development.json y aplica con un F5 (la config
        // se relee sola). En cualquier otro entorno la clave se ignora por diseño.
        if (env.IsDevelopment())
        {
            // Se filtran las entradas en blanco: un `[""]` accidental en el config NO
            // debe reemplazar los roles reales (dejaría al usuario sin acceso).
            var simulados = (config.GetSection("Seguridad:SimularRoles").Get<string[]>() ?? [])
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .ToArray();
            if (simulados.Length > 0)
            {
                roles.Clear();
                roles.AddRange(simulados);
            }
        }

        // Identidad adicional con RoleClaimType estándar: la identidad Windows usa
        // GroupSid como RoleClaimType y no vería claims de rol agregados a ella.
        var identity = new ClaimsIdentity(
            roles.Select(r => new Claim(ClaimTypes.Role, r)),
            authenticationType: Marker) { Label = Marker };
        principal.AddIdentity(identity);
        return principal;
    }
}
