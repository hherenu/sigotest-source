using Microsoft.AspNetCore.Mvc;
using ClosedXML.Excel;
using SIGO.Components.Shared;
using SIGO.Services;

namespace SIGO.Controllers;

/// <summary>
/// La UI de certificados vive en Blazor (Components/Pages/Certificados). Este controller
/// conserva únicamente la descarga de Excel, que es una respuesta de archivo y encaja
/// mejor en un endpoint HTTP que en un componente interactivo.
/// </summary>
[Microsoft.AspNetCore.Authorization.Authorize(Roles = Roles.Certificaciones)]
public class CertificadoController(CertificadoService service) : Controller
{
    // GET: /Certificado/DescargarExcel/5
    [HttpGet]
    public async Task<IActionResult> DescargarExcel(int id)
    {
        var vm = await service.BuildVmAsync(id);
        if (vm is null) return NotFound();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Certificado");

        ws.Cell("A1").Value = "Certificado";
        ws.Cell("B1").Value = vm.Numero;
        ws.Cell("A2").Value = "Periodo";
        ws.Cell("B2").Value = $"{Fmt.MesNombreSeguro(vm.Mes)} {vm.Anio}";
        ws.Cell("A3").Value = "Obra";
        ws.Cell("B3").Value = vm.ObraNombre;
        ws.Cell("A4").Value = "Estado";
        ws.Cell("B4").Value = vm.Estado.ToString();
        ws.Cell("A5").Value = "Fecha Emision";
        ws.Cell("B5").Value = vm.FechaEmision;
        ws.Cell("B5").Style.DateFormat.Format = "dd/MM/yyyy";

        var headerRow = 7;
        void WriteHeader(int r)
        {
            string[] headers =
            [
                "Codigo","Descripcion","Unidad","Cant. Contrato","PU Basico","Monto Contrato",
                "Medicion Anterior","Monto Anterior","Medicion Mes","Monto Mes",
                "Medicion Acumulada","Monto Acumulado","% Acumulado"
            ];
            for (int c = 0; c < headers.Length; c++) ws.Cell(r, c + 1).Value = headers[c];
            ws.Range(r, 1, r, 13).Style.Font.Bold = true;
            ws.Range(r, 1, r, 13).Style.Fill.BackgroundColor = XLColor.LightGray;
        }
        WriteHeader(headerRow);

        var row = headerRow + 1;
        foreach (var bloque in vm.Bloques.OrderBy(b => b.Orden))
        {
            // Título del bloque
            ws.Cell(row, 1).Value = bloque.Titulo;
            ws.Range(row, 1, row, 13).Style.Font.Bold = true;
            ws.Range(row, 1, row, 13).Style.Fill.BackgroundColor = XLColor.LightBlue;
            row++;

            foreach (var (item, nivel) in Arbol.Aplanar(bloque.Items,
                         i => i.AgrupadorPadreId, i => i.ItemEstructuraId, i => i.Orden, i => i.EsAgrupador))
            {
                ws.Cell(row, 1).Value = item.Codigo ?? string.Empty;
                ws.Cell(row, 2).Value = new string(' ', nivel * 2) + item.Descripcion;
                ws.Cell(row, 3).Value = item.Unidad ?? string.Empty;
                ws.Cell(row, 4).Value = item.CantidadContrato ?? 0;
                ws.Cell(row, 5).Value = item.PUBasico ?? 0;
                ws.Cell(row, 6).Value = item.MontoContrato ?? 0;
                ws.Cell(row, 7).Value = item.CantidadAnterior;
                ws.Cell(row, 8).Value = item.MontoAnterior;
                ws.Cell(row, 9).Value = item.CantidadMes;
                ws.Cell(row, 10).Value = item.MontoMes;
                ws.Cell(row, 11).Value = item.CantidadAcumulada;
                ws.Cell(row, 12).Value = item.MontoAcumulado;
                ws.Cell(row, 13).Value = item.PorcentajeAcumulado ?? 0;

                if (item.EsAgrupador)
                {
                    ws.Range(row, 1, row, 13).Style.Font.Bold = true;
                    ws.Range(row, 1, row, 13).Style.Fill.BackgroundColor = XLColor.LightGreen;
                }
                row++;
            }

            // Subtotal del bloque
            ws.Cell(row, 2).Value = $"Subtotal {bloque.Titulo}";
            ws.Cell(row, 6).Value = bloque.TotalMontoContrato;
            ws.Cell(row, 8).Value = bloque.TotalMontoAnterior;
            ws.Cell(row, 10).Value = bloque.TotalMontoMes;
            ws.Cell(row, 12).Value = bloque.TotalMontoAcumulado;
            ws.Range(row, 1, row, 13).Style.Font.Italic = true;
            ws.Range(row, 1, row, 13).Style.Fill.BackgroundColor = XLColor.LightYellow;
            row++;
        }

        ws.Column(2).Width = 70;
        ws.Columns(1, 13).AdjustToContents();
        for (int c = 4; c <= 12; c++) ws.Column(c).Style.NumberFormat.Format = ExcelHelpers.FormatoMoneda;
        ws.Column(13).Style.NumberFormat.Format = "0.00\"%\"";

        var totalRow = row + 1;
        ws.Cell(totalRow, 1).Value = "TOTALES";
        ws.Cell(totalRow, 6).Value = vm.TotalMontoContrato;
        ws.Cell(totalRow, 8).Value = vm.TotalMontoAnterior;
        ws.Cell(totalRow, 10).Value = vm.TotalMontoMes;
        ws.Cell(totalRow, 12).Value = vm.TotalMontoAcumulado;
        ws.Cell(totalRow, 13).Value = vm.PorcentajeAvance;
        ws.Range(totalRow, 1, totalRow, 13).Style.Font.Bold = true;
        ws.Range(totalRow, 1, totalRow, 13).Style.Fill.BackgroundColor = XLColor.LightCyan;
        ws.Cell(totalRow, 6).Style.NumberFormat.Format = ExcelHelpers.FormatoMoneda;
        ws.Cell(totalRow, 8).Style.NumberFormat.Format = ExcelHelpers.FormatoMoneda;
        ws.Cell(totalRow, 10).Style.NumberFormat.Format = ExcelHelpers.FormatoMoneda;
        ws.Cell(totalRow, 12).Style.NumberFormat.Format = ExcelHelpers.FormatoMoneda;
        ws.Cell(totalRow, 13).Style.NumberFormat.Format = "0.00\"%\"";

        var fileName = $"Certificado_{vm.Numero}_{vm.Anio}_{vm.Mes:00}.xlsx";
        return File(ExcelHelpers.ToBytes(wb), ExcelHelpers.ContentType, fileName);
    }
}
