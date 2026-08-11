using SIGO.Services.Validaciones;

namespace SIGO.Models.ViewModels;

/// <summary>Fila calculada para la vista de detalle/carga de un certificado.</summary>
public class ItemCertificadoVM
{
    public int ItemEstructuraId { get; set; }
    public string? Codigo { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public string? Unidad { get; set; }
    public bool EsAgrupador { get; set; }
    public int? AgrupadorPadreId { get; set; }
    public int Orden { get; set; }

    // Contrato
    public decimal? CantidadContrato { get; set; }
    public decimal? PUBasico { get; set; }
    public decimal? MontoContrato { get; set; }

    // Anterior (acumulado de certificados previos cerrados)
    public decimal CantidadAnterior { get; set; }
    public decimal MontoAnterior { get; set; }

    // Mes actual (editable en la vista Cargar; puede ser negativo)
    public decimal CantidadMes { get; set; }
    public decimal MontoMes { get; set; }

    // Acumulado
    public decimal CantidadAcumulada => CantidadAnterior + CantidadMes;
    public decimal MontoAcumulado => MontoAnterior + MontoMes;

    // Porcentajes sobre cantidad contratada (null si es agrupador o sin cantidad).
    // Los asigna el servicio: en borrador el % Mes es el valor guardado tal cual se
    // tipeó (4 decimales, round-trip sin pérdida); cerrado usa el snapshot congelado.
    // El formateo a 2 decimales es responsabilidad de la vista.
    public decimal? PorcentajeAnterior { get; set; }
    public decimal? PorcentajeMes { get; set; }
    public decimal? PorcentajeAcumulado { get; set; }
}

/// <summary>
/// % de avance sobre el monto de contrato, redondeado a 2. ÚNICA implementación:
/// la usan el subtotal de bloque y el total general (pantalla y Excel) — dos
/// redondeos distintos harían que el subtotal no cuadre con el total.
/// </summary>
internal static class Avance
{
    public static decimal Porcentaje(decimal acumulado, decimal contrato) =>
        contrato != 0 ? Math.Round(acumulado * 100 / contrato, 2) : 0;
}

/// <summary>Un bloque del certificado (básico, adicional, BED, …) con sus ítems y subtotales.</summary>
public class BloqueCertificadoVM
{
    public int CertificadoEstructuraId { get; set; }
    public int EstructuraCostosId { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public int Orden { get; set; }
    public List<ItemCertificadoVM> Items { get; set; } = [];

    public decimal TotalMontoContrato { get; set; }
    public decimal TotalMontoAnterior { get; set; }
    public decimal TotalMontoMes { get; set; }
    public decimal TotalMontoAcumulado => TotalMontoAnterior + TotalMontoMes;
    public decimal PorcentajeAvance => Avance.Porcentaje(TotalMontoAcumulado, TotalMontoContrato);
}

/// <summary>
/// ViewModel completo de un certificado (para Cargar, Detalle y el Excel), con N bloques.
/// Datos del certificado aplanados (sin entidades EF): la UI no puede editar por
/// accidente algo trackeado ni acoplarse al esquema (auditoría 2026-07-20, M7).
/// </summary>
public class CertificadoVM
{
    public int CertificadoId { get; set; }
    public int Numero { get; set; }
    public int Mes { get; set; }
    public int Anio { get; set; }
    public Models.Enums.EstadoCertificado Estado { get; set; }
    public DateTime FechaEmision { get; set; }
    public string? Observaciones { get; set; }
    public int ObraId { get; set; }
    public string ObraNombre { get; set; } = string.Empty;

    /// <summary>Token de concurrencia de la sesión de edición (viaja a GuardarItemsAsync).</summary>
    public byte[] RowVersion { get; set; } = [];

    public List<BloqueCertificadoVM> Bloques { get; set; } = [];

    public bool EsEditable => Estado == Models.Enums.EstadoCertificado.Borrador;

    // Totales generales (suma de bloques)
    public decimal TotalMontoContrato => Bloques.Sum(b => b.TotalMontoContrato);
    public decimal TotalMontoAnterior => Bloques.Sum(b => b.TotalMontoAnterior);
    public decimal TotalMontoMes => Bloques.Sum(b => b.TotalMontoMes);
    public decimal TotalMontoAcumulado => TotalMontoAnterior + TotalMontoMes;
    public decimal PorcentajeAvance => Avance.Porcentaje(TotalMontoAcumulado, TotalMontoContrato);
}

/// <summary>
/// Fila del listado de certificados de una obra: proyección de solo lectura sin
/// entidades EF (la grilla bindea esto; las acciones navegan/eliminan por Id).
/// </summary>
public class CertificadoListItemVM
{
    public int Id { get; set; }
    public int Numero { get; set; }
    public int Mes { get; set; }
    public int Anio { get; set; }
    public Models.Enums.EstadoCertificado Estado { get; set; }
    public DateTime FechaEmision { get; set; }
    public string? Observaciones { get; set; }

    /// <summary>Títulos de los bloques ya unidos con ", " (columna "Bloques").</summary>
    public string Bloques { get; set; } = string.Empty;

    /// <summary>
    /// Cerrado o Aprobado: la fila no admite eliminación (gating del botón Eliminar;
    /// la regla vive en <see cref="CertificadoValidator.EsCerrado"/>).
    /// </summary>
    public bool EsCerrado => CertificadoValidator.EsCerrado(Estado);
}

/// <summary>Opción de estructura de costos para dropdowns (alta de certificado, agregar bloque).</summary>
public class EstructuraOpcionVM
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
}

/// <summary>
/// Buffer de edición del alta de certificado: el form bindea esto (no la entidad EF)
/// y CertificadoService.CrearAsync arma una entidad NUEVA por intento — un retry tras
/// un fallo no puede arrastrar el bloque agregado en el intento anterior.
/// </summary>
public class CertificadoNuevoVM
{
    public int Numero { get; set; }
    public int Mes { get; set; }
    public int Anio { get; set; }
    public int EstructuraCostosId { get; set; }
    public DateTime FechaEmision { get; set; }
    public string? Observaciones { get; set; }
}

/// <summary>Contexto de la página de alta: datos de la obra, estructuras elegibles y el form pre-cargado.</summary>
public class CertificadoAltaVM
{
    public int ObraId { get; set; }
    public string ObraNombre { get; set; } = string.Empty;
    public string NumeroLicitacion { get; set; } = string.Empty;
    public List<EstructuraOpcionVM> Estructuras { get; set; } = [];
    public CertificadoNuevoVM Form { get; set; } = new();
}
