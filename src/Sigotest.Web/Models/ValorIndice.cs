namespace SIGO.Models;

/// <summary>
/// Valor mensual de un índice INDEC
/// </summary>
public class ValorIndice
{
    public int Id { get; set; }
    public int IndiceId { get; set; }
    public IndiceINDEC Indice { get; set; } = null!;

    public int Anio { get; set; }

    /// <summary>Mes 1–12</summary>
    public int Mes { get; set; }

    public decimal Valor { get; set; }

    /// <summary>Identificador de la publicación INDEC Informa (ej: INDEC_INFORMA_02_26)</summary>
    public string? IdPublicacion { get; set; }

    /// <summary>Token de concurrencia (rowversion de SQL Server).</summary>
    [System.ComponentModel.DataAnnotations.Timestamp]
    public byte[] RowVersion { get; set; } = [];
}
