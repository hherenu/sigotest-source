using SIGO.Models.Enums;
using SIGO.Services.Validaciones;

namespace SIGO.Models.ViewModels;

public class ItemCalculadoVM
{
    public int Numero { get; set; }
    public string Insumo { get; set; } = string.Empty;
    public decimal PesoPorcentaje { get; set; }
    public decimal? ValorMesBase { get; set; }
    public decimal? ValorMesSalto { get; set; }
    public decimal? KiK0 { get; set; }
    public decimal? VariacionPonderada { get; set; }
    public string? DescripcionINDEC { get; set; }
    public int IndiceId { get; set; }

    // Trazabilidad: de qué registros exactos salió cada valor (se persisten al guardar).
    public int ItemPonderacionId { get; set; }
    public int? ValorIndiceBaseId { get; set; }
    public int? ValorIndiceSaltoId { get; set; }
}

public class RedeterminacionVM
{
    // Datos aplanados (sin entidades EF): ver CertificadoVM.
    public int ObraId { get; set; }
    public string ObraNombre { get; set; } = string.Empty;
    public int TablaPonderacionId { get; set; }

    public List<ItemCalculadoVM> Items { get; set; } = [];

    // Selección de meses / revista
    public int MesBase { get; set; }
    public int AnioBase { get; set; }
    public int MesSalto { get; set; }
    public int AnioSalto { get; set; }
    public string? IdPublicacionBase { get; set; }
    public string? IdPublicacionSalto { get; set; }

    // Totales
    public decimal? TotalKiK0 { get; set; }
    public decimal? PorcentajeAumento { get; set; }

    public bool Calculado { get; set; }
}

/// <summary>
/// Datos administrativos de un disparo guardado (expediente y aprobaciones), aplanados
/// para el form de RedeterminacionAdminDialog: el diálogo no bindea la entidad EF y el
/// guardado pasa por RedeterminacionService.GuardarDatosAdminAsync con el RowVersion como token.
/// </summary>
public class RedeterminacionAdminVM
{
    // Cabecera (solo lectura en el diálogo)
    public int Id { get; set; }
    public int NroDisparo { get; set; }
    public int ObraId { get; set; }
    public string ObraNombre { get; set; } = string.Empty;
    public int MesSalto { get; set; }
    public int AnioSalto { get; set; }
    public decimal? PorcentajeAumento { get; set; }

    // Campos administrativos (editables)
    public string? NroExpedienteVR { get; set; }
    public DateTime? FechaAprobacionCCyR { get; set; }
    public DateTime? FechaAprobacionOS { get; set; }
    public DateTime? FechaLimitePresentacion { get; set; }

    /// <summary>Token de concurrencia de la sesión de edición (viaja a GuardarDatosAdminAsync).</summary>
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// Opción de obra para el módulo Redeterminación (selector de Calcular y filtro del
/// historial). Aplanada: incluye los datos que la ficha de obra de Calcular muestra
/// (y los que necesita el estado computado) sin tocar la entidad. Local del módulo:
/// ObraVM (módulo Obras) es el form completo.
/// </summary>
public class ObraOpcionVM
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string NumeroLicitacion { get; set; } = string.Empty;
    public string? Antecedentes { get; set; }
    public string? Contratista { get; set; }
    public DateTime? FechaActaInicio { get; set; }
    public DateTime? FechaFinalContrato { get; set; }

    /// <summary>Texto de los desplegables de obra: "Nombre — N° de licitación".</summary>
    public string Etiqueta => $"{Nombre} — {NumeroLicitacion}";

    /// <summary>Estado computado ("Proceso Licitatorio"/"Vigente"/"Plazo Vencido"/"Proyectada"); la regla vive en ObraValidator.</summary>
    public string Estado => ObraValidator.Estado(Antecedentes, FechaActaInicio, FechaFinalContrato, DateTime.Today);
}

/// <summary>Opción de tabla de ponderación de una obra (Calcular usa la primera).</summary>
public class TablaOpcionVM
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
}

/// <summary>
/// Fila del historial de disparos (RedeterminacionHistorial): datos del disparo más
/// los de la Obra para el agrupado por obra. El RowVersion del listado viaja a
/// EliminarDisparoAsync como token de concurrencia.
/// </summary>
public class RedeterminacionHistorialVM
{
    public int Id { get; set; }
    public int NroDisparo { get; set; }
    public EstadoRedeterminacion Estado { get; set; }

    // Datos de la Obra (agrupado y título del grupo)
    public int ObraId { get; set; }
    public string ObraNombre { get; set; } = string.Empty;
    public string ObraNumeroLicitacion { get; set; } = string.Empty;

    // Período y resultado
    public int MesSalto { get; set; }
    public int AnioSalto { get; set; }
    public string? IdPublicacionBase { get; set; }
    public string? IdPublicacionSalto { get; set; }
    public decimal? TotalKiK0 { get; set; }
    public decimal? PorcentajeAumento { get; set; }
    public decimal? VariacionAcumulada { get; set; }

    // Datos administrativos
    public string? NroExpedienteVR { get; set; }
    public DateTime? FechaAprobacionCCyR { get; set; }
    public DateTime? FechaAprobacionOS { get; set; }
    public DateTime? FechaLimitePresentacion { get; set; }

    /// <summary>Token de concurrencia del listado (viaja a EliminarDisparoAsync).</summary>
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// Parámetros con los que se guardó un disparo, para que Calcular los precargue al
/// entrar con ?editarId= (obra, publicación y meses del cálculo original).
/// </summary>
public class RedeterminacionParaRecalcularVM
{
    public int ObraId { get; set; }
    public int NroDisparo { get; set; }
    public EstadoRedeterminacion Estado { get; set; }
    /// <summary>false si la obra tiene disparos posteriores: el recálculo no aplica.</summary>
    public bool EsUltimo { get; set; }
    public int AnioBase { get; set; }
    public int MesBase { get; set; }
    public string? IdPublicacionBase { get; set; }
    public int AnioSalto { get; set; }
    public int MesSalto { get; set; }
}

/// <summary>
/// Detalle de un cálculo guardado para RedeterminacionVer: cabecera + snapshot de
/// ítems, aplanado para que la página no bindee la entidad.
/// </summary>
public class RedeterminacionDetalleVM
{
    public int Id { get; set; }
    public int ObraId { get; set; }
    public string ObraNombre { get; set; } = string.Empty;
    public string ObraNumeroLicitacion { get; set; } = string.Empty;

    public int MesBase { get; set; }
    public int AnioBase { get; set; }
    public string? IdPublicacionBase { get; set; }
    public int MesSalto { get; set; }
    public int AnioSalto { get; set; }
    public string? IdPublicacionSalto { get; set; }

    public DateTime FechaGuardado { get; set; }
    public decimal? TotalKiK0 { get; set; }
    public decimal? PorcentajeAumento { get; set; }

    public List<RedeterminacionDetalleItemVM> Items { get; set; } = [];
}

/// <summary>Ítem del snapshot guardado (fila de la tabla de ponderación en Ver).</summary>
public class RedeterminacionDetalleItemVM
{
    public int Id { get; set; }
    public int Numero { get; set; }
    public string Insumo { get; set; } = string.Empty;
    public decimal PesoPorcentaje { get; set; }
    public decimal? ValorMesBase { get; set; }
    public decimal? ValorMesSalto { get; set; }
    public decimal? KiK0 { get; set; }
    public decimal? VariacionPonderada { get; set; }
    public string? DescripcionINDEC { get; set; }
}
