using SIGO.Services.Importacion;

namespace SIGO.Tests;

/// <summary>
/// Arma una <see cref="HojaExcel"/> en memoria con la misma forma que produce
/// <see cref="LectorExcel"/>: cada array es una fila (A, B, C…), null es celda vacía,
/// un array vacío es una fila en blanco (se numera pero no se incluye), los números
/// llegan como double igual que desde ExcelDataReader, y una <see cref="CeldaExcel"/>
/// permite fijar el formato (porcentajes) o un error de Excel. También trae los
/// encabezados de las planillas reales que repiten varios tests.
/// </summary>
public static class HojaExcelBuilder
{
    public static HojaExcel Hoja(params object?[][] filas) => Hoja("Hoja1", filas);

    public static HojaExcel Hoja(string nombre, params object?[][] filas)
    {
        var resultado = new List<FilaExcel>();
        for (var i = 0; i < filas.Length; i++)
        {
            var fila = filas[i];
            var ultima = Array.FindLastIndex(fila, c => c is not null && !(c is string s && s.Length == 0));
            if (ultima < 0) continue;
            var celdas = new CeldaExcel[ultima + 1];
            for (var c = 0; c <= ultima; c++)
                celdas[c] = fila[c] switch
                {
                    null => CeldaExcel.Vacia,
                    CeldaExcel celda => celda,
                    int n => new CeldaExcel((double)n, null),
                    decimal d => new CeldaExcel((double)d, null),
                    double d => new CeldaExcel(d, null),
                    var v => new CeldaExcel(v, null)
                };
            resultado.Add(new FilaExcel(i + 1, celdas));
        }
        return new HojaExcel(nombre, resultado);
    }

    /// <summary>Celda con formato de porcentaje (valor crudo = fracción, como en Excel).</summary>
    public static CeldaExcel Pct(double fraccion) => new(fraccion, "0.00%");

    /// <summary>Celda con un error de Excel («#REF!», «#DIV/0!»), como la entrega el lector.</summary>
    public static CeldaExcel ErrorDeExcel(string codigo) => new(new ErrorExcel(codigo), null);

    /// <summary>La misma hoja, oculta en el libro (como las copias de meses anteriores).</summary>
    public static HojaExcel Oculta(HojaExcel hoja) => new(hoja.Nombre, hoja.Filas, oculta: true);

    /// <summary>Fila en blanco (ocupa número de fila).</summary>
    public static object?[] Blanco => [];

    // ── Encabezados de las planillas reales ─────────────────────────────────────

    /// <summary>Planilla de desglose de Agüero, con los espacios dobles del original.</summary>
    public static readonly object?[] EncabezadoDesglose =
        ["Item", "Descripción", "U", "Cantidad", "Precio    Unitario", "Precio         Subtotal", "TOTAL", "Precio Unitario USD", "Precio Subtotal USD", "TOTAL USD"];

    /// <summary>Planilla de cotización: sin precio unitario (se deriva del subtotal).</summary>
    public static readonly object?[] EncabezadoCotizacion = ["Rubro", "Descripción", "U", "Cantidad", "Subtotal $", "Subtotal USD"];

    /// <summary>Hoja CERTIFICADO de Agüero y Lacroze, primera fila: ACTA DE MEDICIÓN y CERTIFICADO.</summary>
    public static readonly object?[] EncabezadoCertificado =
        [null, "Item", "Descripción", "U", "Cant.", "Precio Unitario", "Precio Subtotal", "ACTA DE MEDICIÓN", null, null, "CERTIFICADO"];

    /// <summary>Hoja CERTIFICADO de Agüero y Lacroze, segunda fila: Anterior/Actual/Acumulado de cada grupo.</summary>
    public static readonly object?[] SubEncabezadoCertificado =
        [null, null, null, null, null, null, null, "Anterior", "Actual", "Acumulado", "Anterior", "Actual", "Acumulado"];

    /// <summary>Hoja CERTIFICADO con Anterior/Actual/Acumulado en la misma fila del encabezado.</summary>
    public static readonly object?[] EncabezadoCertificadoUnaFila =
        [null, "Item", "Descripción", "U", "Cant.", "Precio Unitario", "Precio Subtotal", "ANTERIOR", "ACTUAL", "ACUMULADO", "ANTERIOR", "ACTUAL", "ACUMULADO"];
}
