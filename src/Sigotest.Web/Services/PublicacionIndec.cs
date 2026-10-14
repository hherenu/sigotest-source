namespace SIGO.Services;

/// <summary>
/// Identificador de publicación INDEC ("INDEC_INFORMA_MM_AA"): el sufijo lleva el MES
/// ANTES que el año, así que ordenar los ids como texto ordena por mes y recién después
/// por año ("INDEC_INFORMA_12_26" queda DESPUÉS de "INDEC_INFORMA_01_27"). Mientras
/// hubo publicaciones de un solo año la diferencia no se notaba; con dos años convivendo,
/// el orden alfabético elegía como "última publicación" la de diciembre del año viejo.
///
/// ÚNICA implementación del parseo del sufijo: la usan el orden cronológico de acá y la
/// etiqueta <see cref="Components.Shared.Fmt.PubMayus"/>.
/// </summary>
public static class PublicacionIndec
{
    private const string Prefijo = "INDEC_INFORMA_";

    /// <summary>
    /// Período de la publicación a partir del sufijo "MM_AA" (año de 2 dígitos → 20AA).
    /// false si el id es nulo o no sigue el patrón (publicaciones con otra nomenclatura).
    /// </summary>
    public static bool TryPeriodo(string? idPublicacion, out int anio, out int mes)
    {
        anio = 0; mes = 0;
        if (string.IsNullOrWhiteSpace(idPublicacion)) return false;

        var partes = idPublicacion.Replace(Prefijo, "").Split('_');
        if (partes.Length != 2) return false;
        if (!int.TryParse(partes[0], out var m) || m is < 1 or > 12) return false;
        if (!int.TryParse(partes[1], out var a)) return false;

        anio = a < 100 ? 2000 + a : a;
        mes = m;
        return true;
    }

    /// <summary>
    /// Id bien formado para importar una publicación: exactamente "INDEC_INFORMA_MM_AA",
    /// con mes y año de dos dígitos. Más estricto que <see cref="TryPeriodo"/> (que acepta
    /// el sufijo solo, "04_26", o "8_26" y "08_2026"): el duplicado de publicación se
    /// detecta por texto, así que dos escrituras del mismo período deben ser imposibles.
    /// </summary>
    public static bool EsIdValido(string? idPublicacion) =>
        idPublicacion is not null
        && idPublicacion.Length == Prefijo.Length + 5
        && idPublicacion.StartsWith(Prefijo, StringComparison.Ordinal)
        && idPublicacion[^3] == '_'
        && idPublicacion[^5..^3].All(char.IsAsciiDigit)
        && idPublicacion[^2..].All(char.IsAsciiDigit)
        && TryPeriodo(idPublicacion, out _, out _);

    /// <summary>
    /// Clave de orden CRONOLÓGICO ascendente de una publicación (usar en OrderBy; la
    /// más reciente queda última). Los ids que no siguen el patrón van primero y entre
    /// ellos por texto: así "la última publicación" nunca es una que no se pudo fechar.
    /// </summary>
    public static (int Anio, int Mes, string Id) Orden(string? idPublicacion) =>
        TryPeriodo(idPublicacion, out var anio, out var mes)
            ? (anio, mes, idPublicacion!)
            : (-1, -1, idPublicacion ?? string.Empty);
}

/// <summary>
/// Período "AAAA-M" tal como viaja entre la página Calcular y el export de Excel
/// (dropdowns y querystring). ÚNICA implementación del parseo, con validación de
/// rango (año 2000-2100, mes 1-12): un valor armado a mano no tira FormatException.
/// </summary>
public static class Periodo
{
    public static bool TryParse(string? valor, out int anio, out int mes)
    {
        anio = 0; mes = 0;
        var parts = valor?.Split('-');
        return parts is { Length: 2 }
            && int.TryParse(parts[0], out anio) && anio is >= 2000 and <= 2100
            && int.TryParse(parts[1], out mes) && mes is >= 1 and <= 12;
    }
}
