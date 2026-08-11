namespace SIGO.Services;

/// <summary>
/// "DOMINIO\cuenta" → "cuenta". ÚNICA implementación del recorte de dominio de una
/// identidad Windows (header, tarjeta de Home, auto-registro y resolución en AD).
/// </summary>
public static class CuentaWindows
{
    public static string SinDominio(string windowsUser) => windowsUser.Split('\\').Last();
}
