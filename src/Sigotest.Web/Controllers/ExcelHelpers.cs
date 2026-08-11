using ClosedXML.Excel;

namespace SIGO.Controllers;

/// <summary>
/// Piezas comunes de los exports a Excel. Los layouts de certificados y ponderación
/// son deliberadamente distintos (columnas, colores, jerarquías), así que acá vive
/// solo lo objetivamente compartido: content-type, serialización y formato de moneda.
/// </summary>
public static class ExcelHelpers
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string FormatoMoneda = "#,##0.00";

    public static byte[] ToBytes(XLWorkbook wb)
    {
        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        return stream.ToArray();
    }
}
