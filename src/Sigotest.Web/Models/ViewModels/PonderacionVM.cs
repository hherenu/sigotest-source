namespace SIGO.Models.ViewModels;

/// <summary>Fila de la grilla de insumos de la tabla de ponderación (sin entidades EF).</summary>
public class ItemPonderacionVM
{
    public int Id { get; set; }
    public int Numero { get; set; }
    public string Insumo { get; set; } = string.Empty;
    public decimal PesoPorcentaje { get; set; }
    public int IndiceId { get; set; }
    public string IndiceNombre { get; set; } = string.Empty;
    public string? DescripcionINDEC { get; set; }

    /// <summary>Token de concurrencia de la sesión (viaja a Editar/EliminarItemAsync).</summary>
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>Buffer del form de alta/edición de insumo (nullable para los validators de Radzen).</summary>
public class ItemPonderacionFormVM
{
    public string? Insumo { get; set; }
    public decimal? Peso { get; set; }
    public int? IndiceId { get; set; }
    public string? DescripcionINDEC { get; set; }
}

/// <summary>Opción del catálogo de índices INDEC para el dropdown del form.</summary>
public class IndiceOpcionVM
{
    public int Id { get; set; }
    public string FamiliaRecurso { get; set; } = string.Empty;
    public string CodigoIndice { get; set; } = string.Empty;
}

/// <summary>
/// Estado completo de la página de ponderación de una obra: datos de la obra, la tabla
/// (si existe — hay una sola por obra) y sus insumos. Datos aplanados, ver CertificadoVM.
/// </summary>
public class PonderacionVM
{
    public int ObraId { get; set; }
    public string ObraNombre { get; set; } = string.Empty;
    public string NumeroLicitacion { get; set; } = string.Empty;

    /// <summary>Null si la obra todavía no tiene tabla de ponderación.</summary>
    public int? TablaId { get; set; }
    public string? TablaNombre { get; set; }
    public List<ItemPonderacionVM> Items { get; set; } = [];

    public bool TieneTabla => TablaId is not null;
    public decimal SumaPesos => Items.Sum(i => i.PesoPorcentaje);
}
