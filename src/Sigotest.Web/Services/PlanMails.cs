namespace SIGO.Services;

/// <summary>
/// Piezas compartidas de los mails del circuito de Planificación (transiciones y
/// digest mensual): URL a la pantalla del plan, fallback sin BaseUrl y pie. Antes
/// estaban duplicadas entre PlanificacionService.CuerpoMail y el cuerpo del digest.
/// </summary>
public static class PlanMails
{
    public const string Footer =
        "<p style=\"color:#888;font-size:.85em\">Mensaje automático del sistema SIGO.</p>";

    public const string AccesoFallback = "<p>Entrá al sistema, sección <b>Planificación</b>.</p>";

    /// <summary>URL absoluta a la pantalla del plan de la obra, o null si no hay BaseUrl configurada.</summary>
    public static string? UrlPlan(string? baseUrl, int obraId) =>
        string.IsNullOrWhiteSpace(baseUrl) ? null : $"{baseUrl.TrimEnd('/')}/planificacion/{obraId}/editar";
}
