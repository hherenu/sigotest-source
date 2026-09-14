namespace SIGO.Models;

/// <summary>
/// Prórroga (ampliación de plazo) de una obra: extiende la fecha de fin de contrato.
/// Historial correlativo por obra; <see cref="Obra.FechaFinalContrato"/> es siempre la
/// fecha VIGENTE (la nueva de la última prórroga), así el estado, el horizonte de
/// planificación y los exports no necesitan mirar esta tabla. Solo se elimina la última
/// (restaura <see cref="FechaFinAnterior"/>), misma regla que los disparos de
/// redeterminación: una intermedia rompería el encadenado de fechas.
/// </summary>
public class ProrrogaObra : BaseEntity
{
    public int ObraId { get; set; }
    public Obra Obra { get; set; } = null!;

    /// <summary>Correlativo dentro de la obra (1, 2, 3…).</summary>
    public int Numero { get; set; }

    /// <summary>Fin de contrato vigente al momento de otorgarla (lo que la prórroga extiende).</summary>
    public DateTime FechaFinAnterior { get; set; }

    /// <summary>Nueva fecha de fin de contrato. Siempre posterior a la anterior.</summary>
    public DateTime FechaFinNueva { get; set; }

    /// <summary>Fecha del acto administrativo que la otorga (opcional).</summary>
    public DateTime? FechaActo { get; set; }

    /// <summary>N° de documento GDE del acto (IF-…), opcional.</summary>
    public string? IFActo { get; set; }

    public string? Observaciones { get; set; }
}
