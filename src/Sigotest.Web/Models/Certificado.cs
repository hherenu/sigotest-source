using SIGO.Models.Enums;

namespace SIGO.Models;

public class Certificado : BaseEntity
{
    public int ObraId { get; set; }
    public Obra Obra { get; set; } = null!;

    /// <summary>Número de certificado (1, 2, 3…)</summary>
    public int Numero { get; set; }

    public int Mes { get; set; }
    public int Anio { get; set; }

    public DateTime FechaEmision { get; set; } = DateTime.Today;

    /// <summary>Estado del certificado. El snapshot se congela al pasar a Cerrado.</summary>
    public EstadoCertificado Estado { get; set; } = EstadoCertificado.Borrador;

    /// <summary>Cuándo se congeló el snapshot (al cerrar). Null mientras es borrador.</summary>
    public DateTime? FechaCierre { get; set; }

    public string? Observaciones { get; set; }

    /// <summary>Bloques del certificado (básico, adicional, BED, …).</summary>
    public ICollection<CertificadoEstructura> Estructuras { get; set; } = [];
}

public class ItemCertificado
{
    public int Id { get; set; }

    public int CertificadoEstructuraId { get; set; }
    public CertificadoEstructura CertificadoEstructura { get; set; } = null!;

    public int ItemEstructuraId { get; set; }
    public ItemEstructura ItemEstructura { get; set; } = null!;

    /// <summary>Tipo de movimiento (se deriva del ítem de estructura, overridable).</summary>
    public TipoMovimiento TipoMovimiento { get; set; } = TipoMovimiento.Normal;

    // ── Movimiento del mes (firmado — puede ser negativo: BED/economías/compensaciones) ──
    public decimal PorcentajeActual { get; set; }
    public decimal CantidadActual { get; set; }
    public decimal MontoActual { get; set; }

    // ── Snapshot — null mientras es borrador, congelado al cerrar ──────────────────────
    public decimal? PorcentajeAnterior { get; set; }
    public decimal? CantidadAnterior { get; set; }
    public decimal? MontoAnterior { get; set; }
    public decimal? PorcentajeAcumulado { get; set; }
    public decimal? CantidadAcumulada { get; set; }
    public decimal? MontoAcumulado { get; set; }

    // Valores de contrato congelados al cerrar: sin esto, editar la estructura después
    // cambiaba retroactivamente el detalle y el Excel de certificados ya emitidos.
    public decimal? CantidadContrato { get; set; }
    public decimal? PUBasico { get; set; }
    public decimal? MontoContrato { get; set; }

    public string? Observaciones { get; set; }

    /// <summary>Token de concurrencia (rowversion de SQL Server).</summary>
    [System.ComponentModel.DataAnnotations.Timestamp]
    public byte[] RowVersion { get; set; } = [];
}
