namespace SIGO.Services.Validaciones;

/// <summary>
/// Reglas de valores mensuales de índices INDEC, puras y sin acceso a datos (mismo
/// esquema que <see cref="PonderacionValidator"/>): el servicio lanza si devuelven
/// error; la UI puede evaluarlas para mostrar el mensaje.
/// </summary>
public static class IndiceValidator
{
    /// <summary>
    /// Un valor mensual debe ser mayor que cero: 0 como divisor rompe el Ki/K0 del mes
    /// base, y como dividendo produce un coeficiente 0 que entra a la cadena de
    /// VariaciónAcumulada. ÚNICA implementación de la regla — la usan el alta manual
    /// (AgregarValorAsync) y la importación de publicaciones (ImportadorIndec); el diálogo
    /// de alta la evalúa tal cual con un RadzenCustomValidator (docs/convenciones-ui.md).
    /// </summary>
    public static string? ValorMensual(decimal? valor) => valor is > 0 ? null : ValorNoPositivo;

    public const string ValorNoPositivo = "El valor debe ser mayor que cero.";

    /// <summary>Rango de años de los valores mensuales (alta manual e importación).</summary>
    public const int AnioMinimo = 2000;
    public const int AnioMaximo = 2100;

    public static readonly string AnioFueraDeRango = $"El año debe estar entre {AnioMinimo} y {AnioMaximo}.";

    public static string? Anio(int anio) => anio is >= AnioMinimo and <= AnioMaximo ? null : AnioFueraDeRango;

    /// <summary>
    /// Período de un valor mensual: año en rango y mes 1-12. El alta manual no lo revisaba
    /// en el servicio (solo el Min/Max del numérico de la página, que lleva el valor al
    /// límite sin avisar) y un mes 13 guardado rompería las pantallas que muestran el
    /// período (Fmt.MesCorto no lo admite).
    /// </summary>
    public static string? Periodo(int anio, int mes) =>
        Anio(anio) ?? (mes is >= 1 and <= 12 ? null : "El mes debe estar entre 1 y 12.");

    /// <summary>
    /// Alta de índice: obligatorios whitespace-safe (los RequiredValidator de Radzen
    /// dejan pasar espacios). Un código con espacios o vacío rompe el match exacto de
    /// la importación de publicaciones y deja entradas en blanco en los dropdowns.
    /// </summary>
    public static string? Guardar(string? codigoIndice, string? familiaRecurso)
    {
        if (string.IsNullOrWhiteSpace(codigoIndice))
            return "El código del índice es obligatorio.";
        if (string.IsNullOrWhiteSpace(familiaRecurso))
            return "La familia de recurso es obligatoria.";
        return null;
    }

    /// <summary>
    /// ID de publicación de la importación de publicaciones (/indices/importar):
    /// obligatorio y con el formato INDEC_INFORMA_MM_AA. Sin id, los valores quedaban
    /// invisibles como publicación en la pantalla de calcular (filtra IdPublicacion !=
    /// null) y mezclados con los valores base; con un id no fechable, la publicación
    /// nunca gana como "más reciente" (<see cref="PublicacionIndec.Orden"/>).
    /// NO rige para el alta manual de valores: ahí la publicación vacía es legítima
    /// (tasas Banco Nación, que se cargan de a una por esa vía — decisión 2026-10-07).
    /// </summary>
    public static string? PublicacionImportacion(string? idPublicacion) =>
        string.IsNullOrWhiteSpace(idPublicacion)
            ? "El ID de publicación es obligatorio."
            : PublicacionIndec.EsIdValido(idPublicacion.Trim())
                ? null
                : "El ID de publicación debe tener el formato INDEC_INFORMA_MM_AA (ej.: INDEC_INFORMA_08_26).";

    /// <summary>Código único en el catálogo: mensaje de negocio antes del error crudo del índice único.</summary>
    public static string? CodigoDuplicado(string codigo, bool existe) =>
        existe ? $"Ya existe un índice con el código «{codigo}»." : null;
}
