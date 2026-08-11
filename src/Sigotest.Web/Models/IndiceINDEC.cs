namespace SIGO.Models;

/// <summary>
/// Índice INDEC — corresponde a una fila del Cuadro 5 del INDEC Informa (Decreto 1295/2002)
/// </summary>
public class IndiceINDEC
{
    public int Id { get; set; }

    /// <summary>Identificador único del índice (ej: MANO_OBRA, ALBANILERIA)</summary>
    public string CodigoIndice { get; set; } = string.Empty;

    /// <summary>Nombre de la familia de recurso (ej: Mano de obra)</summary>
    public string FamiliaRecurso { get; set; } = string.Empty;

    /// <summary>Nombre normalizado según INDEC Informa (ej: a) Mano de obra)</summary>
    public string? IndiceNormalizado { get; set; }

    /// <summary>Cuadro de referencia INDEC (ej: Cuadro 1.4, Cuadro 1.5)</summary>
    public string? CuadroReferencia { get; set; }

    /// <summary>Código de inciso del Decreto 1295/2002 (ej: a, b, d, p)</summary>
    public string? IncisoCode { get; set; }

    public ICollection<ValorIndice> Valores { get; set; } = [];
}
