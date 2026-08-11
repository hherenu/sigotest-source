namespace SIGO.Models.ViewModels;

/// <summary>Datos mínimos de la obra para las cabeceras del módulo de estructuras.</summary>
public record ObraResumenVM(int Id, string Nombre, string NumeroLicitacion);

/// <summary>
/// Estructura de costos aplanada (sin entidades EF): la usan las cards del listado
/// (Items vacío; TotalItems/MontoTotal calculados por query) y la página de ítems
/// (Items completo + datos de la obra para la cabecera).
/// </summary>
public class EstructuraVM
{
    public int Id { get; set; }
    public int ObraId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public DateTime FechaCreacion { get; set; }

    /// <summary>Token de concurrencia de la carga: viaja al DELETE de la estructura.</summary>
    public byte[] RowVersion { get; set; } = [];

    // Cabecera de la página de ítems (vacíos en las cards del listado).
    public string ObraNombre { get; set; } = string.Empty;
    public string ObraNumeroLicitacion { get; set; } = string.Empty;

    /// <summary>Cantidad de ítems hoja (los agrupadores no cuentan).</summary>
    public int TotalItems { get; set; }

    /// <summary>Suma de montos de los ítems hoja.</summary>
    public decimal MontoTotal { get; set; }

    /// <summary>Ítems ordenados por Orden (la UI los aplana con Arbol.Aplanar).</summary>
    public List<ItemEstructuraVM> Items { get; set; } = [];
}

/// <summary>
/// Datos del formulario de alta de estructura. El nombre arranca en "Estructura
/// Original" (mismo prefill que tenía el default de la entidad al bindearla directo).
/// </summary>
public class EstructuraFormVM
{
    public string? Nombre { get; set; } = "Estructura Original";
    public DateTime FechaCreacion { get; set; } = DateTime.Today;
}

/// <summary>
/// Ítem de estructura aplanado: sirve para la grilla jerárquica, el form de alta y el
/// de edición (los strings admiten null porque los TextBox pueden devolverlo; la
/// normalización del validator los resuelve al guardar).
/// </summary>
public class ItemEstructuraVM
{
    public int Id { get; set; }
    public int EstructuraCostosId { get; set; }
    public bool EsAgrupador { get; set; }
    public int? AgrupadorPadreId { get; set; }
    public int Orden { get; set; }
    public string? Codigo { get; set; }
    public string? Descripcion { get; set; }
    public string? Unidad { get; set; }
    public decimal? Cantidad { get; set; }
    public decimal? PUBasico { get; set; }
    public decimal? Monto { get; set; }

    /// <summary>Token de concurrencia de la carga: viaja al UPDATE/DELETE del ítem.</summary>
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>Opción del dropdown "Agrupador padre" (agrupadores de la misma estructura).</summary>
public record AgrupadorOpcionVM(int Id, string Descripcion);

/// <summary>Carga de la página de edición de ítem: el ítem y sus padres posibles.</summary>
public record ItemParaEditarVM(ItemEstructuraVM Item, List<AgrupadorOpcionVM> Agrupadores);
