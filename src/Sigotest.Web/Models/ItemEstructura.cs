using SIGO.Models.Enums;

namespace SIGO.Models;

public class ItemEstructura
{
    public int Id { get; set; }
    public int EstructuraCostosId { get; set; }
    public EstructuraCostos EstructuraCostos { get; set; } = null!;

    /// <summary>Tipo de movimiento del ítem (Normal, Adicional, Demasia, Economia, …).</summary>
    public TipoMovimiento TipoMovimiento { get; set; } = TipoMovimiento.Normal;

    /// <summary>
    /// FK self-referencial al ítem original afectado, cuando este ítem representa un
    /// BED/economía/compensación sobre un ítem de la estructura básica. Null si es propio.
    /// </summary>
    public int? ItemOrigenId { get; set; }
    public ItemEstructura? ItemOrigen { get; set; }

    /// <summary>true = cabecera de rubro (no tiene Cantidad/PU, solo suma hijos)</summary>
    public bool EsAgrupador { get; set; }

    /// <summary>FK self-referencial: agrupador al que pertenece este ítem (null = raíz)</summary>
    public int? AgrupadorPadreId { get; set; }
    public ItemEstructura? AgrupadorPadre { get; set; }
    public ICollection<ItemEstructura> Hijos { get; set; } = [];

    /// <summary>Posición en la tabla (determina el orden de visualización)</summary>
    public int Orden { get; set; }

    /// <summary>Código del ítem, ej: P.GEN.1.1 (null para agrupadores)</summary>
    public string? Codigo { get; set; }

    public string Descripcion { get; set; } = string.Empty;

    // ── Campos solo para ítems hoja (EsAgrupador = false) ──────────────────────

    public string? Unidad { get; set; }
    public decimal? Cantidad { get; set; }
    public decimal? PUBasico { get; set; }

    /// <summary>Monto contratado = Cantidad × PUBasico (almacenado para evitar recalcular)</summary>
    public decimal? Monto { get; set; }

    /// <summary>Token de concurrencia (rowversion de SQL Server).</summary>
    [System.ComponentModel.DataAnnotations.Timestamp]
    public byte[] RowVersion { get; set; } = [];
}
