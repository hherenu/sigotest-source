using SIGO.Models.Enums;

namespace SIGO.Models;

public class RedeterminacionGuardada : BaseEntity
{
    public int ObraId { get; set; }
    public Obra Obra { get; set; } = null!;
    public int TablaPonderacionId { get; set; }
    public TablaPonderacion TablaPonderacion { get; set; } = null!;

    /// <summary>Estado del trámite de la redeterminación.</summary>
    public EstadoRedeterminacion Estado { get; set; } = EstadoRedeterminacion.Borrador;

    public DateTime FechaGuardado { get; set; }

    public int MesBase { get; set; }
    public int AnioBase { get; set; }
    public string? IdPublicacionBase { get; set; }

    public int MesSalto { get; set; }
    public int AnioSalto { get; set; }
    public string? IdPublicacionSalto { get; set; }

    public decimal? TotalKiK0 { get; set; }
    public decimal? PorcentajeAumento { get; set; }
    public decimal? VariacionAcumulada { get; set; }

    public int NroDisparo { get; set; }

    // Campos administrativos
    public string? NroExpedienteVR { get; set; }
    public DateTime? FechaAprobacionCCyR { get; set; }
    public DateTime? FechaAprobacionOS { get; set; }
    public DateTime? FechaLimitePresentacion { get; set; }

    public ICollection<RedeterminacionGuardadaItem> Items { get; set; } = [];
}

public class RedeterminacionGuardadaItem
{
    public int Id { get; set; }
    public int RedeterminacionGuardadaId { get; set; }
    public RedeterminacionGuardada Redeterminacion { get; set; } = null!;

    public int Numero { get; set; }
    public string Insumo { get; set; } = string.Empty;
    public decimal PesoPorcentaje { get; set; }
    public decimal? ValorMesBase { get; set; }
    public decimal? ValorMesSalto { get; set; }
    public decimal? KiK0 { get; set; }
    public decimal? VariacionPonderada { get; set; }
    public string? DescripcionINDEC { get; set; }

    // ── Trazabilidad: punteros a los registros exactos usados en el cálculo ──────
    // Nullable y sin cascade: el snapshot (valores de arriba) es la fuente de verdad;
    // estas FK solo permiten reconstruir de qué registros salió cada valor.
    public int? ItemPonderacionId { get; set; }
    public int? IndiceId { get; set; }
    public int? ValorIndiceBaseId { get; set; }
    public int? ValorIndiceSaltoId { get; set; }

    /// <summary>Token de concurrencia (rowversion de SQL Server).</summary>
    [System.ComponentModel.DataAnnotations.Timestamp]
    public byte[] RowVersion { get; set; } = [];
}
