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

    /// <summary>
    /// El tema si es válido; si no (cookie ausente o adulterada), el claro por defecto. La cookie
    /// solo existe si el usuario usó el AppearanceToggle: el SetTheme inicial de RadzenTheme
    /// ocurre antes de que CookieThemeService se suscriba, así que no la escribe.
    /// </summary>
    public static string Normalizar(string? tema) =>
        string.Equals(tema, Oscuro, StringComparison.OrdinalIgnoreCase) ? Oscuro : Claro;
}
