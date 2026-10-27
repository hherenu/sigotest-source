using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SIGO.Data;
using SIGO.Models;
using SIGO.Models.Enums;
using SIGO.Services.Validaciones;
using static System.Net.WebUtility;

namespace SIGO.Services;

/// <summary>
/// Mail 1 del circuito de Planificación: el día configurado (Correo:DiaDigest, default
/// 10) de cada mes envía UN digest a cada director con la lista de sus obras y el
/// estado del plan de cada una. Si la app estuvo apagada ese día, lo envía al arrancar
/// (catch-up dentro del mismo mes); el registro en DigestsPlanificacionEnviados evita
/// re-envíos por reinicios. Sale recién con el rollover del mes registrado, porque
/// informa el ciclo nuevo: si Correo:DiaRollover es posterior a DiaDigest, se demora
/// hasta ese día.
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
        await EjecutarAsync(db, emailSender, opciones.CurrentValue.BaseUrl, hoy, logger, ct);
    }

    /// <summary>
    /// Envía el digest del mes de <paramref name="hoy"/> si corresponde y devuelve los
    /// mails enviados; null si no salió: ya se envió, el rollover del mes todavía no
    /// corrió, otra instancia lo está enviando, o fallaron todos los envíos (la pasada
    /// siguiente reintenta). Separado del hosted service para poder testearlo contra la
    /// DB real.
    /// </summary>
    public static async Task<int?> EjecutarAsync(AppDbContext db, IEmailSender emailSender, string? baseUrl,
        DateTime hoy, ILogger logger, CancellationToken ct = default)
    {
        if (await db.DigestsPlanificacionEnviados.AnyAsync(d => d.Anio == hoy.Year && d.Mes == hoy.Month, ct))
            return null; // el digest de este mes ya salió

        // El digest informa el estado del ciclo nuevo: sale recién con el rollover del mes
        // registrado. Con la app apagada los días del rollover y del digest, al arrancar
        // corrían los dos a la vez y el mail mostraba los estados de antes del reinicio.
        if (!await db.RolloversPlanificacionEjecutados.AnyAsync(r => r.Anio == hoy.Year && r.Mes == hoy.Month, ct))
            return null;

        // Todo lo que lee la base va ANTES de reservar: después de la reserva solo quedan
        // los envíos, que atrapan su propio error. Un corte de la base entre la reserva y
        // la consulta dejaba el mes reservado sin ningún mail y sin reintento.
        var (porDirector, sinDirector) = await DestinatariosAsync(db, hoy, ct);

        // El mes se reserva ANTES de enviar: con dos procesos (reciclado superpuesto de
        // IIS) el índice único deja pasar a uno solo. Antes se registraba al final y los
        // dos enviaban el digest completo. Mientras se envía queda en -1 (ver la entidad).
        var registro = new DigestPlanificacionEnviado
        {
            Anio = hoy.Year, Mes = hoy.Month, FechaEnvio = DateTime.UtcNow, CantidadMails = DigestPlanificacionEnviado.EnCurso
        };
        db.DigestsPlanificacionEnviados.Add(registro);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (SqlErrores.EsClaveDuplicada(ex))
        {
            return null; // otra instancia lo reservó primero: su corrida vale
        }

        int enviados;
        try
        {
            enviados = await EnviarAsync(emailSender, porDirector, baseUrl, hoy, logger);
        }
        catch
        {
            await LiberarAsync(db, registro, logger);
            throw;
        }

        // Con el SMTP caído o todas las direcciones inválidas (0 de N mails reales),
        // registrar igual perdía el digest del mes definitivamente: se libera la reserva
        // y la pasada siguiente reintenta (y toma una dirección recién corregida). Con
        // éxito parcial queda registrado: reintentar duplicaría los mails que ya salieron
        // (los fallos individuales quedan logueados).
        if (porDirector.Count > 0 && enviados == 0)
        {
            if (await LiberarAsync(db, registro, logger))
                logger.LogWarning("Digest de planificación {Mes:00}/{Anio}: fallaron los {Total} envíos; " +
                    "no se registra el mes y se reintenta en la próxima pasada.", hoy.Month, hoy.Year, porDirector.Count);
            return null;
        }

        registro.CantidadMails = enviados;
        registro.FechaEnvio = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None); // los mails ya salieron: el registro no se cancela

        logger.LogInformation("Digest de planificación {Mes:00}/{Anio}: {Mails} mail(s) a {Directores} director(es); " +
            "{SinDirector} obra(s) sin correo de director.", hoy.Month, hoy.Year, enviados, porDirector.Count, sinDirector);
        return enviados;
    }

    /// <summary>Quita la reserva del mes para que la pasada siguiente reintente; false si no se pudo.</summary>
    private static async Task<bool> LiberarAsync(AppDbContext db, DigestPlanificacionEnviado registro, ILogger logger)
    {
        try
        {
            db.DigestsPlanificacionEnviados.Remove(registro);
            await db.SaveChangesAsync(CancellationToken.None);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "No se pudo liberar la reserva del digest de planificación {Mes:00}/{Anio}: " +
                "este mes no se reintenta.", registro.Mes, registro.Anio);
            return false;
        }
    }

    /// <summary>Las obras del recordatorio agrupadas por correo de director, y cuántas no tienen director.</summary>
    private static async Task<(Dictionary<string, List<ObraDigest>> PorDirector, int SinDirector)> DestinatariosAsync(
        AppDbContext db, DateTime hoy, CancellationToken ct)
    {
        var obras = await db.Obras.AsNoTracking()
            // Una obra "Proyectada" todavía no se planifica: no entra en el recordatorio.
            .Where(ObraValidator.EnPlanificacion(hoy))
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

        var sinDirector = obras.Count(o => string.IsNullOrWhiteSpace(o.EmailDirector)
                                           && string.IsNullOrWhiteSpace(o.CorreoDirectorObra));
        return (porDirector, sinDirector);
    }

    /// <summary>Envía un digest por director (sin tocar la base) y devuelve cuántos salieron.</summary>
    private static async Task<int> EnviarAsync(IEmailSender emailSender, Dictionary<string, List<ObraDigest>> porDirector,
        string? baseUrl, DateTime hoy, ILogger logger)
    {
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
                if (await emailSender.EnviarAsync([correo], asunto, Cuerpo(lista, baseUrl)) > 0)
                    enviados++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falló el digest de planificación a {Correo}.", correo);
            }
        }
        return enviados;
    }

    private static string Cuerpo(List<ObraDigest> obras, string? baseUrl)
    {
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
