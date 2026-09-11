using SIGO.Services.Validaciones;

namespace SIGO.Models.ViewModels;

/// <summary>
/// Obra aplanada para la UI (grilla y formulario), sin entidad EF: la página no puede
/// editar por accidente algo trackeado ni acoplarse al esquema (mismo criterio que
/// CertificadoVM, auditoría 2026-07-20, M7).
/// </summary>
public class ObraVM
{
    public int Id { get; set; }

    public string Nombre { get; set; } = string.Empty;
    public string NumeroLicitacion { get; set; } = string.Empty;
    public string? Antecedentes { get; set; }
    public string? Contratista { get; set; }
    public string? PUByC { get; set; }
    public DateTime? FechaOferta { get; set; }
    public string? OfertaFinal { get; set; }
    public DateTime? FechaAdjudicacion { get; set; }
    public string? IFAdjudicacion { get; set; }
    public DateTime? FechaContrato { get; set; }
    public string? IFContrato { get; set; }
    public DateTime? FechaActaInicio { get; set; }
    public string? IFActaInicio { get; set; }
    public string? PlazoObra { get; set; }
    public DateTime? FechaFinalContrato { get; set; }
    public string? DirectorObra { get; set; }
    /// <summary>Presupuesto oficial en pesos (obligatorio, mayor a cero); US$ y € opcionales.</summary>
    public decimal PresupuestoOficial { get; set; }
    public decimal? PresupuestoOficialUSD { get; set; }
    public decimal? PresupuestoOficialEUR { get; set; }
    /// <summary>Presupuesto adjudicado por moneda (opcional; cargado, manda sobre el oficial en Planificación).</summary>
    public decimal? PresupuestoAdjudicado { get; set; }
    public decimal? PresupuestoAdjudicadoUSD { get; set; }
    public decimal? PresupuestoAdjudicadoEUR { get; set; }

    /// <summary>Los seis importes con la regla de referencia (ver <see cref="PresupuestosObra"/>).</summary>
    public PresupuestosObra Presupuestos => new(
        PresupuestoOficial, PresupuestoOficialUSD, PresupuestoOficialEUR,
        PresupuestoAdjudicado, PresupuestoAdjudicadoUSD, PresupuestoAdjudicadoEUR);
    public int? DirectorUsuarioId { get; set; }
    /// <summary>Nombre del director para mostrar (usuario asignado, o el texto libre). Solo lectura del detalle.</summary>
    public string? DirectorNombre { get; set; }
    public string? IFDesignacion { get; set; }

    /// <summary>Token de concurrencia de la sesión: viaja al UPDATE/DELETE del servicio.</summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>Cantidad de tablas de ponderación (columna "Tablas"; solo la carga el listado).</summary>
    public int CantidadTablas { get; set; }

    /// <summary>Estado computado ("Proceso Licitatorio"/"Vigente"/"Plazo Vencido"/"Proyectada"); la regla vive en ObraValidator.</summary>
    public string Estado => ObraValidator.Estado(Antecedentes, FechaActaInicio, FechaFinalContrato, DateTime.Today);
}

/// <summary>Detalle de la obra (solo lectura) con los resúmenes de estructuras y tablas.</summary>
public class ObraDetalleVM : ObraVM
{
    /// <summary>Estructuras de costos ordenadas por fecha de creación.</summary>
    public List<EstructuraObraVM> EstructurasCostos { get; set; } = [];

    public List<TablaPonderacionObraVM> TablasPonderacion { get; set; } = [];
}

/// <summary>Resumen de una estructura de costos para el detalle de la obra.</summary>
public record EstructuraObraVM(int Id, string Nombre, DateTime FechaCreacion);

/// <summary>Tabla de ponderación con sus ítems (ya ordenados por número) para el detalle.</summary>
public class TablaPonderacionObraVM
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public List<ItemPonderacionObraVM> Items { get; set; } = [];
}

/// <summary>Fila de la tabla de ponderación en el detalle de la obra.</summary>
public record ItemPonderacionObraVM(int Numero, string Insumo, decimal PesoPorcentaje, string? DescripcionINDEC);

/// <summary>Opción del dropdown "Director (usuario)": Id + Nombre, sin entidad Usuario.</summary>
public record DirectorCandidatoVM(int Id, string Nombre);
