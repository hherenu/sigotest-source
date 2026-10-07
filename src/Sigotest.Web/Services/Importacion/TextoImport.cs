using System.Globalization;
using System.Text;

namespace SIGO.Services.Importacion;

/// <summary>
/// Normalización de textos y helpers de encabezado compartidos por los dos importadores
/// (estructuras y certificados), para que una regla o un arreglo vivan en un solo lugar.
/// </summary>
public static class TextoImport
{
    /// <summary>Filas con datos en las que se busca el encabezado de la planilla.</summary>
    public const int FilasDeBusquedaDeEncabezado = 80;

    /// <summary>Minúsculas, sin acentos, espacios internos colapsados y recortado.</summary>
    public static string Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return string.Empty;
        var descompuesto = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(descompuesto.Length);
        var espacioPendiente = false;
        foreach (var ch in descompuesto)
        {
            var categoria = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (categoria == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsWhiteSpace(ch))
            {
                espacioPendiente = sb.Length > 0;
                continue;
            }
            if (espacioPendiente) { sb.Append(' '); espacioPendiente = false; }
            sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Clave de comparación de códigos de ítem: recortado, mayúsculas y sin espacios
    /// internos (los códigos vienen copiados a mano entre planillas).
    /// </summary>
    public static string ClaveCodigo(string? codigo) =>
        string.Concat((codigo ?? string.Empty).Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();

    /// <summary>
    /// true si el texto parece un código de ítem: tiene algún dígito y no tiene
    /// espacios (o es corto y todo en mayúsculas, como «PEV-SD-EAG-AD 01.1»). Los títulos
    /// de rubro tienen espacios y minúsculas o no tienen dígitos; «TOTAL RUBRO 1» o
    /// «SUBTOTAL 01.1» nunca son códigos.
    /// </summary>
    public static bool PareceCodigo(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return false;
        var t = texto.Trim();
        if (!t.Any(char.IsDigit)) return false;
        if (!t.Any(char.IsWhiteSpace)) return true;
        return t.Length <= 25 && !t.Any(char.IsLower) && !Normalizar(t).Contains("total");
    }

    /// <summary>
    /// Texto de una fila de cierre: dice «total» (también «subtotal», al principio o al
    /// final) y no es un código. Cada importador decide en qué columnas lo busca.
    /// </summary>
    public static bool EsTextoDeTotal(string texto) =>
        texto.Length > 0 && !PareceCodigo(texto) && Normalizar(texto).Contains("total");

    // ── Encabezados ─────────────────────────────────────────────────────────────

    /// <summary>Celdas de la fila normalizadas (para buscar encabezados por contenido).</summary>
    public static string[] Normalizadas(FilaExcel fila) =>
        fila.Celdas.Select(c => Normalizar(c.Texto)).ToArray();

    /// <summary>
    /// Fila con datos que sigue a la del índice dado (la segunda fila de un encabezado
    /// de dos filas), o null si no hay.
    /// </summary>
    public static FilaExcel? FilaSiguiente(HojaExcel hoja, int indice) =>
        indice + 1 < hoja.Filas.Count ? hoja.Filas[indice + 1] : null;

    /// <summary>Encabezado de la columna de unidad: «U», «Un», «Ud», «Unidad».</summary>
    public static bool EsEncabezadoUnidad(string normalizado) =>
        normalizado is "u" or "u." or "un" or "ud" || normalizado.StartsWith("unidad");

    /// <summary>
    /// Columnas Item/Rubro y Descripción de una fila de encabezado, o false si no es
    /// encabezado (la descripción debe ir después del código).
    /// </summary>
    public static bool EsFilaDeEncabezado(string[] normalizadas, out int item, out int descripcion)
    {
        item = Array.FindIndex(normalizadas, x => x is "item" or "rubro");
        descripcion = Array.FindIndex(normalizadas, x => x.StartsWith("descripcion"));
        return item >= 0 && descripcion > item;
    }

    /// <summary>
    /// Array.FindIndex desde una columna que puede venir de otra fila: las filas se
    /// recortan en su última celda con dato, así que arrancar fuera de rango es «no está»
    /// (-1), no una excepción que bloquee el libro entero por una hoja ajena.
    /// </summary>
    public static int Buscar(string[] normalizadas, int desde, Predicate<string> condicion) =>
        desde >= normalizadas.Length ? -1 : Array.FindIndex(normalizadas, desde, condicion);

    /// <summary>Índice de Array.FindIndex a opcional (-1 → null).</summary>
    public static int? Opcional(int indice) => indice >= 0 ? indice : null;

    /// <summary>
    /// Texto del desplegable de hojas, igual en las dos importaciones: el nombre, lo que se
    /// reconoció («Planilla de desglose», «N°15 jul/2026», «sin renglones») y las marcas
    /// «oculta» y «recortada».
    /// </summary>
    public static string EtiquetaHoja(HojaExcel hoja, string? detalle)
    {
        var etiqueta = detalle is null ? hoja.Nombre : $"{hoja.Nombre} — {detalle}";
        if (hoja.Oculta) etiqueta += " · oculta";
        if (hoja.Recortada) etiqueta += $" · recortada a {LectorExcel.MaxFilasConDatos.ToString("N0", EsAr)} filas";
        return etiqueta;
    }

    private static readonly CultureInfo EsAr = CultureInfo.GetCultureInfo("es-AR");
}
