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
    /// (AgregarValorAsync) y la importación de publicaciones (ImportadorIndec).
    /// </summary>
    public static string? ValorMensual(decimal? valor) =>
        valor is > 0 ? null : "El valor debe ser mayor que cero.";

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

    /// <summary>Código único en el catálogo: mensaje de negocio antes del error crudo del índice único.</summary>
    public static string? CodigoDuplicado(string codigo, bool existe) =>
        existe ? $"Ya existe un índice con el código «{codigo}»." : null;
}
