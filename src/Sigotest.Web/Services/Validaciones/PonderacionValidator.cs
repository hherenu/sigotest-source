namespace SIGO.Services.Validaciones;

/// <summary>
/// Reglas de la tabla de ponderación y sus insumos, puras y sin acceso a datos (mismo
/// esquema que <see cref="CertificadoValidator"/>): el servicio les pasa lo que ya leyó
/// y lanza si devuelven error; la UI puede evaluarlas para deshabilitar acciones.
/// </summary>
public static class PonderacionValidator
{
    /// <summary>
    /// Alta/edición de un insumo: nombre obligatorio, peso en (0, 100] e índice INDEC
    /// obligatorio (espejo servidor de los validators del form de PonderacionEditar).
    /// </summary>
    public static string? GuardarItem(string? insumo, decimal? peso, int? indiceId)
    {
        if (string.IsNullOrWhiteSpace(insumo))
            return "El insumo es obligatorio.";
        if (peso is null || peso <= 0m || peso > 100m)
            return "El peso debe ser mayor que 0 y como máximo 100.";
        if (indiceId is null)
            return "Seleccioná el índice INDEC.";
        return null;
    }

    /// <summary>
    /// Trazabilidad (FK Restrict): un insumo referenciado por redeterminaciones guardadas
    /// no puede eliminarse — mensaje específico antes del error de FK.
    /// </summary>
    public static string? EliminarItem(string insumo, int usos) =>
        usos > 0
            ? $"El insumo «{insumo}» está referenciado por {usos} ítem(s) de redeterminaciones guardadas y no puede eliminarse."
            : null;
}
