using Microsoft.Extensions.Options;

namespace SIGO.Services;

/// <summary>Config de la sección "Correo" de appsettings.</summary>
public class CorreoOptions
{
    /// <summary>Interruptor general: en false los mails solo se loguean (no se envían).</summary>
    public bool Habilitado { get; set; }

    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 25;
    public bool UsarSsl { get; set; }

    /// <summary>Credenciales del relay. Vacío = conexión anónima (relay interno típico).</summary>
    public string? Usuario { get; set; }
    public string? Password { get; set; }

    public string Remitente { get; set; } = string.Empty;
    public string? RemitenteNombre { get; set; }

    /// <summary>URL pública de la app para armar links en los mails (ej. http://servidor:5056). Vacío = sin link.</summary>
    public string? BaseUrl { get; set; }

    public int TimeoutSegundos { get; set; } = 10;

    /// <summary>Día del mes en que sale el digest de planificación a los directores.</summary>
    public int DiaDigest { get; set; } = 10;

    /// <summary>
    /// Día del mes del rollover del ciclo de planificación (resetea las tomas de
    /// conocimiento). La víspera del digest: el mail del día siguiente arranca el
    /// ciclo nuevo con los flags ya limpios.
    /// </summary>
    public int DiaRollover { get; set; } = 9;
}

/// <summary>Envío de correo. Implementación por SMTP; los llamadores deciden qué hacer ante un fallo.</summary>
public interface IEmailSender
{
    /// <summary>
    /// Envía un mail HTML a los destinatarios (se depuran blancos y duplicados).
    /// Sin destinatarios, o con el correo deshabilitado en config, solo loguea.
    /// Devuelve la cantidad de destinatarios efectivamente incluidos: 0 significa
    /// que NO salió ningún mail (p.ej. todas las direcciones eran inválidas) —
    /// los llamadores que registran "enviado" deben mirar este valor, no solo
    /// la ausencia de excepción.
    /// </summary>
    Task<int> EnviarAsync(IReadOnlyCollection<string> destinatarios, string asunto, string cuerpoHtml);
}

/// <summary>
/// Sender contra el relay SMTP corporativo (System.Net.Mail alcanza para un relay
/// interno; si algún día se necesita OAuth/entrega moderna, cambiar esta clase por
/// una implementación MailKit sin tocar a los llamadores).
/// </summary>
public class SmtpEmailSender(IOptionsMonitor<CorreoOptions> opciones, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task<int> EnviarAsync(IReadOnlyCollection<string> destinatarios, string asunto, string cuerpoHtml)
    {
        var cfg = opciones.CurrentValue;
        var dest = destinatarios
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => d.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (dest.Count == 0)
        {
            logger.LogWarning("Correo «{Asunto}» sin destinatarios: no se envía. " +
                "Revisar que los usuarios del rol tengan Email cargado en /usuarios.", asunto);
            return 0;
        }

        if (!cfg.Habilitado || string.IsNullOrWhiteSpace(cfg.SmtpHost))
        {
            logger.LogInformation("Correo deshabilitado (Correo:Habilitado/SmtpHost): se habría enviado " +
                "«{Asunto}» a: {Destinatarios}.", asunto, string.Join(", ", dest));
            // Se informa como "incluidos": con el correo apagado los llamadores no
            // deben reintentar por siempre (el digest registraría el mes igual).
            return dest.Count;
        }

        using var cliente = new System.Net.Mail.SmtpClient(cfg.SmtpHost, cfg.SmtpPort) { EnableSsl = cfg.UsarSsl };
        if (!string.IsNullOrWhiteSpace(cfg.Usuario))
            cliente.Credentials = new System.Net.NetworkCredential(cfg.Usuario, cfg.Password);

        using var mensaje = new System.Net.Mail.MailMessage
        {
            From = new System.Net.Mail.MailAddress(cfg.Remitente, cfg.RemitenteNombre ?? cfg.Remitente),
            Subject = asunto,
            Body = cuerpoHtml,
            IsBodyHtml = true,
            SubjectEncoding = System.Text.Encoding.UTF8,
            BodyEncoding = System.Text.Encoding.UTF8
        };
        foreach (var d in dest)
        {
            // Una dirección malformada (dato legado, ej. CorreoDirectorObra interino)
            // no debe perder el mail para los demás: se descarta con aviso.
            try { mensaje.To.Add(d); }
            catch (FormatException)
            {
                logger.LogWarning("Dirección inválida descartada del correo «{Asunto}»: {Direccion}.", asunto, d);
            }
        }
        if (mensaje.To.Count == 0)
        {
            logger.LogWarning("Correo «{Asunto}»: ninguna dirección válida; no se envía.", asunto);
            return 0;
        }

        // SmtpClient.Timeout no aplica a SendMailAsync: el corte real es el token.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(cfg.TimeoutSegundos));
        await cliente.SendMailAsync(mensaje, cts.Token);
        logger.LogInformation("Correo «{Asunto}» enviado a {Cantidad} destinatario(s).", asunto, mensaje.To.Count);
        return mensaje.To.Count;
    }
}
