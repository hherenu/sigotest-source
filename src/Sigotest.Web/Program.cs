using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.EntityFrameworkCore;
using Radzen;
using SIGO.Components;
using SIGO.Components.Shared;
using SIGO.Data;
using SIGO.Services;

// La app es Windows-only por diseño (autenticación Negotiate/Kerberos contra el
// dominio y consulta del email en AD): se declara para que el analizador de
// plataformas no exija guards en cada uso de las API de Windows.
[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]

var builder = WebApplication.CreateBuilder(args);

// Factory scoped: cada operación crea un AppDbContext de vida corta (evita el contexto
// longevo por circuito de Blazor Server). Scoped para poder resolver ICurrentUser del scope.
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("SigotestDb")),
    ServiceLifetime.Scoped);

// ── Autenticación Windows (Negotiate/Kerberos): los usuarios del dominio entran con
// su sesión de Windows, sin login. La FallbackPolicy exige usuario autenticado en toda
// la app (no hay páginas públicas); las descargas dejan de ser enumerables anónimamente.
// En "Testing" no se registra Negotiate: los tests de contrato instalan su propio
// esquema para poder verificar el 401 sin depender del handshake de Windows.
var authentication = builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme);
if (!builder.Environment.IsEnvironment("Testing"))
{
    authentication.AddNegotiate();
}
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = options.DefaultPolicy;
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

// Autorización por roles de aplicación: los roles salen de la tabla Usuarios (no de
// grupos AD) y se inyectan como claims al autenticar. Bootstrap de Admin en
// RolesClaimsTransformation (tabla vacía o Seguridad:AdminsIniciales).
builder.Services.AddScoped<Microsoft.AspNetCore.Authentication.IClaimsTransformation, RolesClaimsTransformation>();
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddScoped<RedeterminacionService>();
builder.Services.AddScoped<CertificadoService>();
builder.Services.AddScoped<PlanificacionService>();
// ABM simples movidos de las páginas a servicios (patrón SAF: la UI no toca
// entidades ni hace SaveChanges; reglas puras en Services/Validaciones).
builder.Services.AddScoped<ObraService>();
builder.Services.AddScoped<EstructuraService>();
builder.Services.AddScoped<IndiceService>();
builder.Services.AddScoped<UsuarioService>();
builder.Services.AddScoped<PonderacionService>();

// ── Correo (circuito de Planificación): sender SMTP + destinatarios desde Usuarios.
// Con Correo:Habilitado=false los mails solo se loguean (transiciones funcionan igual).
builder.Services.Configure<CorreoOptions>(builder.Configuration.GetSection("Correo"));
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
// Singleton: sin estado; consulta AD por sAMAccountName en el auto-registro.
builder.Services.AddSingleton<IDirectoryEmailResolver, ActiveDirectoryEmailResolver>();
builder.Services.AddScoped<IPlanNotificationRecipients, UsuariosNotificationRecipients>();
// Digest mensual del día 10 (Correo:DiaDigest) a los directores, con idempotencia en DB.
builder.Services.AddHostedService<PlanificacionDigestService>();
// Rollover del ciclo mensual el día 9 (Correo:DiaRollover, víspera del digest):
// resetea las tomas de conocimiento salvo obras finalizadas. Idempotencia en DB.
builder.Services.AddHostedService<PlanificacionRolloverService>();

// La UI es 100% Blazor + Radzen. Solo quedan controllers para las descargas de Excel
// (Certificado/Redeterminacion/PlanificacionExport), que devuelven File() y no usan vistas.
builder.Services.AddControllers();

// ── Blazor (Interactive Server) + Radzen ──────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddRadzenComponents();
// El tema elegido con el AppearanceToggle (claro/oscuro) persiste en una cookie,
// para que sobreviva al F5 y a los reinicios del circuito.
builder.Services.AddRadzenCookieThemeService(options =>
{
    options.Name = "SIGOTheme";
    options.Duration = TimeSpan.FromDays(365);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    // La página de error se sirve como HTML estático, no como componente Blazor: si la app
    // falló, comunicarlo no debe depender de que se pueda levantar un circuito.
    app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(PaginaError.Html(context.TraceIdentifier));
    }));
    app.UseHsts();
}

app.UseHttpsRedirection();

// ── Headers de seguridad: con Negotiate el navegador auto-autentica también los
// iframes (zona intranet) — sin anti-framing, cualquier página externa puede embeber
// la app ya autenticada y dirigir clics a botones reales (clickjacking).
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Content-Security-Policy"] = "frame-ancestors 'none'";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    await next();
});

// Un 403 de autorización en una carga de página completa (F5 o URL directa) no pasa
// por el router de Blazor: sin esto el navegador muestra su error crudo y el usuario
// queda sin navegación. Se lo redirige a /sin-acceso (accesible a todo autenticado),
// que ofrece los módulos habilitados para su rol.
app.UseStatusCodePages(context =>
{
    if (context.HttpContext.Response.StatusCode == StatusCodes.Status403Forbidden)
        context.HttpContext.Response.Redirect("/sin-acceso");
    return Task.CompletedTask;
});

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();

// ── Contratos del sitio (sigotest-source): /health y /Error son los únicos endpoints
// anónimos. /health es prueba de vida sin datos sensibles (sin connection strings ni
// config); /Error sirve el mismo HTML estático del exception handler, por si el proxy
// o la infraestructura lo consultan directo.
app.MapGet("/health", () => Results.Json(new
{
    status = "ok",
    application = "SIGO",
    machine = Environment.MachineName,
    framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    utc = DateTimeOffset.UtcNow
})).AllowAnonymous();

app.MapGet("/Error", (HttpContext context) =>
    Results.Content(PaginaError.Html(context.TraceIdentifier), "text/html; charset=utf-8"))
   .AllowAnonymous();

// Endpoints de descarga de Excel (sin landing MVC: la raíz "/" la sirve Blazor).
app.MapControllerRoute(
    name: "default",
    pattern: "{controller}/{action}/{id?}");

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Aplicar migraciones (y seed) automáticamente al arrancar, en TODOS los entornos:
// decisión deliberada para el despliegue interno — el deploy es copiar y arrancar,
// sin paso manual de "dotnet ef database update". En "Testing" se omite: los tests
// de contrato bootean la app sin base de datos.
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    using var ctx = factory.CreateDbContext();
    ctx.Database.Migrate();
}

app.Run();

// Visible para WebApplicationFactory<Program> en los tests de contrato.
public partial class Program;
