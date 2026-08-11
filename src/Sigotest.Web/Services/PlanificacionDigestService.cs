using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SIGO.Data;
using SIGO.Models;
using SIGO.Models.Enums;
using static System.Net.WebUtility;

namespace SIGO.Services;

/// <summary>
/// Mail 1 del circuito de Planificación: el día configurado (Correo:DiaDigest, default
/// 10) de cada mes envía UN digest a cada director con la lista de sus obras y el
/// estado del plan de cada una. Si la app estuvo apagada ese día, lo envía al arrancar
/// (catch-up dentro del mismo mes); el registro en DigestsPlanificacionEnviados evita
/// re-envíos por reinicios.
/// </summary>
public class PlanificacionDigestService(
    IServiceProvider services,
    IOptionsMonitor<CorreoOptions> opciones,
    ILogger<PlanificacionDigestService> logger) : TareaMensualHostedService(logger)
{
    private static readonly CultureInfo EsAr = CultureInfo.GetCultureInfo("es-AR");

    protected override string Descripcion => "el chequeo del digest de planificación";

    protected override async Task ChequearAsync(CancellationToken ct)
    {
        var hoy = DateTime.Today;
        if (hoy.Day < opciones.CurrentValue.DiaDigest) return;

        // BackgroundService es singleton: los servicios scoped se resuelven por corrida.
        using var scope = services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.DigestsPlanificacionEnviados.AnyAsync(d => d.Anio == hoy.Year && d.Mes == hoy.Month, ct))
            return; // el digest de este mes ya salió

        var obras = await db.Obras.AsNoTracking()
            // Una obra finalizada (flag del plan) queda fuera del ciclo mensual: ni
            // rollover ni recordatorio.
            .Where(o => !db.Planificaciones.Any(p => p.ObraId == o.Id && p.ObraFinalizada))
            .Select(o => new
            {
                o.Id,
                o.Nombre,
                o.NumeroLicitacion,
                EmailDirector = o.DirectorUsuario != null && o.DirectorUsuario.Activo
                    ? o.DirectorUsuario.Email : null,
                o.CorreoDirectorObra,
                Estado = db.Planificaciones.Where(p => p.ObraId == o.Id)
                    .Select(p => (EstadoPlanificacion?)p.Estado).FirstOrDefault()
            })
            .ToListAsync(ct);

        // Un digest por correo de director, con TODAS sus obras (criterio único de
        // resolución en UsuariosNotificationRecipients.CorreosDirector).
        var porDirector = new Dictionary<string, List<ObraDigest>>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in obras)
        {
            foreach (var correo in UsuariosNotificationRecipients.CorreosDirector(o.EmailDirector, o.CorreoDirectorObra))
            {
                if (!porDirector.TryGetValue(correo, out var lista))
                    porDirector[correo] = lista = [];
                lista.Add(new ObraDigest(o.Id, o.Nombre, o.NumeroLicitacion, o.Estado));
            }
        }

        var asunto = $"Planificación de obras — actualización mensual ({EsAr.DateTimeFormat.GetMonthName(hoy.Month)} {hoy.Year})";
        var enviados = 0;
        foreach (var (correo, lista) in porDirector)
        {
            // Un fallo con un director no debe frenar el resto del reparto.
            try
            {
                // Solo cuenta si el mail salió de verdad: EnviarAsync devuelve 0 cuando
                // la dirección era inválida y se descartó (dato legado sin @). Contarlo
                // como enviado registraba el mes con cero mails y sin reintento.
                if (await emailSender.EnviarAsync([correo], asunto, Cuerpo(lista)) > 0)
                    enviados++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falló el digest de planificación a {Correo}.", correo);
            }
        }

        // El mes se registra solo si no quedó TODO sin enviar: con el SMTP caído
        // o todas las direcciones inválidas durante la corrida completa (0 de N
        // mails reales), registrar igual perdía el digest del mes definitivamente;
        // ahora la pasada horaria siguiente reintenta (y toma una dirección recién
        // corregida). Con éxito parcial sí se registra (reintentar duplicaría los
        // mails que ya salieron; los fallos individuales quedan logueados arriba).
        if (porDirector.Count > 0 && enviados == 0)
        {
            logger.LogWarning("Digest de planificación {Mes:00}/{Anio}: fallaron los {Total} envíos; " +
                "no se registra el mes y se reintenta en la próxima pasada.", hoy.Month, hoy.Year, porDirector.Count);
            return;
        }

        db.DigestsPlanificacionEnviados.Add(new DigestPlanificacionEnviado
        {
            Anio = hoy.Year,
            Mes = hoy.Month,
            FechaEnvio = DateTime.UtcNow,
            CantidadMails = enviados
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Otra instancia registró el mes primero (índice único): su corrida vale.
        }

        var sinDirector = obras.Count(o => string.IsNullOrWhiteSpace(o.EmailDirector)
                                           && string.IsNullOrWhiteSpace(o.CorreoDirectorObra));
        logger.LogInformation("Digest de planificación {Mes:00}/{Anio}: {Mails} mail(s) a {Directores} director(es); " +
            "{SinDirector} obra(s) sin correo de director.", hoy.Month, hoy.Year, enviados, porDirector.Count, sinDirector);
    }

    private string Cuerpo(List<ObraDigest> obras)
    {
        var baseUrl = opciones.CurrentValue.BaseUrl;
        var filas = string.Join("", obras.OrderBy(o => o.Nombre).Select(o =>
        {
            var nombre = $"{HtmlEncode(o.Nombre)} ({HtmlEncode(o.Licitacion)})";
            var link = PlanMails.UrlPlan(baseUrl, o.Id) is string url
                ? $"<a href=\"{url}\">{nombre}</a>"
                : nombre;
            return $"<li>{link} — <b>{EstadoTexto(o.Estado)}</b></li>";
        }));

        return "<p>Te pedimos que cargues o revises la planificación (curva de inversión prevista) de tus obras:</p>" +
               $"<ul>{filas}</ul>" +
               "<p>Cuando termines, marcá cada plan como <b>Cargada</b> para que el Gerente de Obras lo controle.</p>" +
               (string.IsNullOrWhiteSpace(baseUrl) ? PlanMails.AccesoFallback : "") +
               PlanMails.Footer;
    }

    private static string EstadoTexto(EstadoPlanificacion? e) => e switch
    {
        null => "Sin iniciar",
        EstadoPlanificacion.Pendiente => "Pendiente de carga",
        EstadoPlanificacion.Cargada => "Cargada (en control del gerente)",
        EstadoPlanificacion.EnRevision => "En revisión — requiere correcciones",
        EstadoPlanificacion.Aprobada => "Aprobada",
        _ => e.ToString()!
    };

    private sealed record ObraDigest(int Id, string Nombre, string Licitacion, EstadoPlanificacion? Estado);
}
