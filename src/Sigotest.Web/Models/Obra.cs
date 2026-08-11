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
