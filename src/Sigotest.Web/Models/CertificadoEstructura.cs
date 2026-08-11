namespace SIGO.Models;

/// <summary>
/// Bloque de un certificado: agrupa los ítems ejecutados de una estructura de costos
/// (básico, adicional, BED, …). Un certificado puede tener varios bloques.
/// </summary>
public class CertificadoEstructura
{
    public int Id { get; set; }

    public int CertificadoId { get; set; }
    public Certificado Certificado { get; set; } = null!;

    public int EstructuraCostosId { get; set; }
    public EstructuraCostos EstructuraCostos { get; set; } = null!;

    /// <summary>Posición del bloque dentro del certificado.</summary>
    public int Orden { get; set; }

    /// <summary>Título del bloque (ej "Básico", "BED N°1"). Si null, se usa EstructuraCostos.Nombre.</summary>
    public string? Titulo { get; set; }

    // ── Snapshot de subtotales — null mientras es borrador, congelado al cerrar ──────────
    public decimal? SubtotalAnterior { get; set; }
    public decimal? SubtotalActual { get; set; }
    public decimal? SubtotalAcumulado { get; set; }

    public ICollection<ItemCertificado> Items { get; set; } = [];

    /// <summary>Token de concurrencia (rowversion de SQL Server).</summary>
    [System.ComponentModel.DataAnnotations.Timestamp]
    public byte[] RowVersion { get; set; } = [];
}
