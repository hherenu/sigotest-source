using SIGO.Models.Enums;

namespace SIGO.Models;

public class EstructuraCostos : BaseEntity
{
    public int ObraId { get; set; }
    public Obra Obra { get; set; } = null!;

    /// <summary>Ej: "Estructura Original", "Adicional N°1"</summary>
    public string Nombre { get; set; } = "Estructura Original";

    /// <summary>Naturaleza del bloque de costos (Basico, Adicional, BED, …).</summary>
    public TipoEstructura Tipo { get; set; } = TipoEstructura.Basico;

    /// <summary>Número de orden dentro del tipo (ej: "Adicional N°1" → 1). Null para el básico.</summary>
    public int? Numero { get; set; }

    /// <summary>Acto administrativo que la aprueba (ej: RESDI-2026-170-GCABA-SBASE).</summary>
    public string? ActoAdministrativo { get; set; }

    public string? Expediente { get; set; }

    public DateTime? FechaAprobacion { get; set; }

    public string? Observaciones { get; set; }

    public DateTime FechaCreacion { get; set; } = DateTime.Today;

    public ICollection<ItemEstructura> Items { get; set; } = [];
}
