namespace SIGO.Models.ViewModels;

/// <summary>
/// Índice del catálogo INDEC aplanado para la grilla y el formulario (sin entidades EF:
/// la UI no puede editar por accidente algo trackeado ni acoplarse al esquema, mismo
/// criterio que CertificadoVM).
/// </summary>
public class IndiceVM
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
}

/// <summary>Valor mensual de un índice para la grilla de /indices/{id}/valores.</summary>
public class ValorIndiceVM
{
    public int Id { get; set; }
    public int Anio { get; set; }

    /// <summary>Mes 1–12</summary>
    public int Mes { get; set; }

    public decimal Valor { get; set; }

    /// <summary>Identificador de la publicación INDEC Informa (ej: INDEC_INFORMA_02_26)</summary>
    public string? IdPublicacion { get; set; }

    /// <summary>
    /// Token de concurrencia leído con la grilla: viaja al DELETE para detectar
    /// ediciones concurrentes desde que la página lo mostró.
    /// </summary>
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// Formulario de alta de un valor mensual. El Valor nullable obliga al usuario a
/// cargarlo (el RequiredValidator de la página se apoya en el null).
/// </summary>
public class NuevoValorVM
{
    public int Anio { get; set; }
    public int Mes { get; set; } = 1;
    public decimal? Valor { get; set; }
    public string? IdPublicacion { get; set; }
}
