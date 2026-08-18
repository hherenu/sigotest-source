namespace SIGO.Services;

/// <summary>
/// Los dos temas Radzen de la app y la normalización de la cookie SIGOTheme.
/// Única fuente: la usan App.razor (tema inicial del SSR, evita el flash) y
/// MainLayout (corrige una cookie adulterada que dejaría a RadzenTheme
/// apuntando a un CSS inexistente).
/// </summary>
public static class Temas
{
    public const string Oscuro = "material-dark";
    public const string Claro = "material";

    /// <summary>El tema si es válido; si no (cookie ausente o adulterada), el oscuro por defecto.</summary>
    public static string Normalizar(string? tema) =>
        string.Equals(tema, Claro, StringComparison.OrdinalIgnoreCase) ? Claro
        : string.Equals(tema, Oscuro, StringComparison.OrdinalIgnoreCase) ? Oscuro
        : Oscuro;
}
