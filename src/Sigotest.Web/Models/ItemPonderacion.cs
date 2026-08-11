namespace SIGO.Models;

public class ItemPonderacion
{
    public int Id { get; set; }
    public int TablaPonderacionId { get; set; }
    public TablaPonderacion TablaPonderacion { get; set; } = null!;

    /// <summary>Nro de fila (1, 2, 3…)</summary>
    public int Numero { get; set; }

    /// <summary>Nombre del insumo (ej: Mano de Obra, Albañilería…)</summary>
    public string Insumo { get; set; } = string.Empty;

    /// <summary>Peso en porcentaje (ej: 10.00 para 10%)</summary>
    public decimal PesoPorcentaje { get; set; }

    public int IndiceId { get; set; }
    public IndiceINDEC Indice { get; set; } = null!;

    /// <summary>Descripción INDEC Informa tal como aparece en la tabla (texto libre)</summary>
    public string? DescripcionINDEC { get; set; }

    /// <summary>Token de concurrencia (rowversion de SQL Server).</summary>
    [System.ComponentModel.DataAnnotations.Timestamp]
    public byte[] RowVersion { get; set; } = [];
}
