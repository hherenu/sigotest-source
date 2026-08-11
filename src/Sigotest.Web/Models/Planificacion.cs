using SIGO.Models.Enums;

namespace SIGO.Models;

/// <summary>
/// Planificación de la curva de inversión (cash-flow previsto) de una obra. Reemplaza al
/// circuito de Flokzu. Contexto separado del módulo de Obras/Certificaciones: lo único
/// compartido es la identidad de <see cref="Obra"/>. Un único plan editable por obra.
/// </summary>
public class Planificacion : BaseEntity
{
    public int ObraId { get; set; }
    public Obra Obra { get; set; } = null!;

    /// <summary>Estado del circuito de carga/control/aprobación. Ver <see cref="EstadoPlanificacion"/>.</summary>
    public EstadoPlanificacion Estado { get; set; } = EstadoPlanificacion.Pendiente;

    /// <summary>Cuándo el director dejó el plan como "Cargada" por última vez.</summary>
    public DateTime? FechaCarga { get; set; }

    /// <summary>Cuándo el gerente aprobó el plan.</summary>
    public DateTime? FechaAprobacion { get; set; }

    // ── Sub-flujo "enviar a revisar" (Gerente → EnRevision) ──────────────────────
    public string? MotivoRevision { get; set; }
    public string? Correcciones { get; set; }

    // ── Toma de conocimiento (Presupuesto) ───────────────────────────────────────
    // Cierra el ciclo mensual: al tomar conocimiento de un plan Aprobado se genera el
    // snapshot del mes. El rollover del día Correo:DiaRollover resetea estos campos
    // (salvo obra finalizada) para que el ciclo del mes nuevo vuelva a requerirla.
    public DateTime? FechaTomaConocimiento { get; set; }
    public string? TomadaConocimientoPor { get; set; }

    /// <summary>
    /// Obra terminada a los efectos de la planificación: queda fuera del ciclo mensual
    /// (ni rollover ni digest). Campo propio del plan, como en el formulario de Flokzu:
    /// la Obra no tiene un estado "finalizada" persistido.
    /// </summary>
    public bool ObraFinalizada { get; set; }

    /// <summary>Bloques del plan: Básica / Adicional N / BED N.</summary>
    public ICollection<Autorizante> Autorizantes { get; set; } = [];
}

/// <summary>
/// Bloque de una planificación: la Obra Básica, un Adicional o un BED. Entidad propia y
/// liviana del contexto Planificación — NO se relaciona con <c>EstructuraCostos</c> del
/// módulo de obras. El nombre sigue el vocabulario del sistema hermano del IVC.
/// </summary>
public class Autorizante : BaseEntity
{
    public int PlanificacionId { get; set; }
    public Planificacion Planificacion { get; set; } = null!;

    /// <summary>Naturaleza del bloque (Basica / Adicional / BED).</summary>
    public TipoAutorizante Tipo { get; set; } = TipoAutorizante.Basica;

    /// <summary>Orden dentro del tipo (Adicional 1/2/3, BED 1/2/3). Null para la básica.</summary>
    public int? Numero { get; set; }

    /// <summary>Denominación / asociado a la obra (si aplica).</summary>
    public string? Denominacion { get; set; }

    /// <summary>Montos del bloque: autorizado, anticipo, curva mensual y cálculos anuales.</summary>
    public ICollection<PlanMonto> Montos { get; set; } = [];
}

/// <summary>
/// Una fila de monto de la planificación. Colapsa toda la grilla del formulario de Flokzu
/// (bloque × concepto × período × moneda) en una sola tabla hija. El período depende del
/// <see cref="Concepto"/>: Mensual usa Anio+Mes; CalculoAnual solo Anio; AnticipoFinanciero
/// lleva opcionalmente Anio+Mes (mes de pago); MontoAutorizado no lleva período.
/// </summary>
public class PlanMonto
{
    public int Id { get; set; }

    public int AutorizanteId { get; set; }
    public Autorizante Autorizante { get; set; } = null!;

    public ConceptoPlanMonto Concepto { get; set; } = ConceptoPlanMonto.Mensual;

    public int? Anio { get; set; }
    public int? Mes { get; set; }

    public Moneda Moneda { get; set; } = Moneda.Pesos;

    public decimal Monto { get; set; }

    /// <summary>Token de concurrencia (rowversion de SQL Server).</summary>
    [System.ComponentModel.DataAnnotations.Timestamp]
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// Foto inmutable de la planificación de una obra en un mes: se genera cuando
/// Presupuesto toma conocimiento del plan Aprobado (sin toma no hay foto). Una por
/// obra y mes; una nueva toma en el mismo mes (tras un loop de revisión) la reemplaza.
/// Los montos se denormalizan en <see cref="PlanMontoSnapshot"/> para que la historia
/// no dependa del plan vivo (que se sigue editando).
/// </summary>
public class PlanificacionSnapshot
{
    public int Id { get; set; }

    public int ObraId { get; set; }
    public Obra Obra { get; set; } = null!;

    /// <summary>Mes de la versión (el "Nro planificacion" del circuito Flokzu).</summary>
    public int Anio { get; set; }
    public int Mes { get; set; }

    public DateTime FechaTomaConocimiento { get; set; }
    public string? TomadaConocimientoPor { get; set; }

    public ICollection<PlanMontoSnapshot> Montos { get; set; } = [];
}

/// <summary>
/// Un monto de un snapshot. Copia denormalizada de la celda del plan (bloque ×
/// concepto × período × moneda) sin FK a <see cref="Autorizante"/>: editar o borrar
/// bloques del plan vivo no toca la historia.
/// </summary>
public class PlanMontoSnapshot
{
    public int Id { get; set; }

    public int PlanificacionSnapshotId { get; set; }
    public PlanificacionSnapshot Snapshot { get; set; } = null!;

    public TipoAutorizante Tipo { get; set; }
    public int? Numero { get; set; }
    public string? Denominacion { get; set; }

    public ConceptoPlanMonto Concepto { get; set; }
    public int? Anio { get; set; }
    public int? Mes { get; set; }
    public Moneda Moneda { get; set; }
    public decimal Monto { get; set; }
}

/// <summary>
/// Registro del rollover mensual del ciclo de planificación (día Correo:DiaRollover,
/// la víspera del digest): idempotencia del hosted service, igual que
/// <see cref="DigestPlanificacionEnviado"/>. Tabla de sistema.
/// </summary>
public class RolloverPlanificacionEjecutado
{
    public int Id { get; set; }
    public int Anio { get; set; }
    public int Mes { get; set; }
    public DateTime FechaEjecucion { get; set; }
    /// <summary>Cantidad de planes cuya toma de conocimiento se reinició en esa corrida.</summary>
    public int CantidadPlanesReiniciados { get; set; }
}

/// <summary>
/// Registro del digest mensual del día 10 a los directores (uno por mes): es la
/// idempotencia del hosted service — si la app se reinicia ese día, no re-envía.
/// Tabla de sistema (sin BaseEntity: no la escribe ningún usuario).
/// </summary>
public class DigestPlanificacionEnviado
{
    public int Id { get; set; }
    public int Anio { get; set; }
    public int Mes { get; set; }
    public DateTime FechaEnvio { get; set; }
    /// <summary>Cantidad de mails (uno por director) que salieron en esa corrida.</summary>
    public int CantidadMails { get; set; }
}
