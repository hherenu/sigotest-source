namespace SIGO.Models;

public class Obra : BaseEntity
{
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

    /// <summary>
    /// Presupuesto oficial en pesos. Obligatorio (mayor a cero) al guardar la ficha; las
    /// obras anteriores a la columna quedan en 0 hasta que se editen.
    /// </summary>
    public decimal PresupuestoOficial { get; set; }
    /// <summary>Presupuesto oficial en dólares (opcional).</summary>
    public decimal? PresupuestoOficialUSD { get; set; }
    /// <summary>Presupuesto oficial en euros (opcional).</summary>
    public decimal? PresupuestoOficialEUR { get; set; }

    /// <summary>
    /// Presupuesto adjudicado en pesos (null hasta la adjudicación). Con adjudicado
    /// cargado, la planificación de la Obra Básica se controla contra él en todas las
    /// monedas; si no, contra el oficial (regla en <see cref="PresupuestosObra"/>).
    /// </summary>
    public decimal? PresupuestoAdjudicado { get; set; }
    /// <summary>Presupuesto adjudicado en dólares (opcional).</summary>
    public decimal? PresupuestoAdjudicadoUSD { get; set; }
    /// <summary>Presupuesto adjudicado en euros (opcional).</summary>
    public decimal? PresupuestoAdjudicadoEUR { get; set; }

    /// <summary>Los seis importes como valor con la regla de referencia. No mapeado (sin setter).</summary>
    public PresupuestosObra Presupuestos => PresupuestosObra.De(this);

    /// <summary>Correo(s) del director de obra para las notificaciones de planificación. Lista separada por coma.</summary>
    public string? CorreoDirectorObra { get; set; }

    /// <summary>
    /// Director de obra como usuario de la app (destinatario del circuito de
    /// planificación). DirectorObra/CorreoDirectorObra quedan como texto libre
    /// interino para obras sin usuario asignado.
    /// </summary>
    public int? DirectorUsuarioId { get; set; }
    public Usuario? DirectorUsuario { get; set; }

    /// <summary>
    /// Nombre del director a mostrar: el usuario asignado; si no hay, el texto libre
    /// interino. Requiere DirectorUsuario cargado (Include); no traducible a SQL
    /// (en proyecciones EF va el condicional inline).
    /// </summary>
    public string? DirectorNombre => DirectorUsuario?.Nombre ?? DirectorObra;
    public string? IFDesignacion { get; set; }

    public ICollection<TablaPonderacion> TablasPonderacion { get; set; } = [];
    public ICollection<EstructuraCostos> EstructurasCostos { get; set; } = [];
}
