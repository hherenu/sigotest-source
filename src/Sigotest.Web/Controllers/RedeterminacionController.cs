using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SIGO.Components.Shared;
using SIGO.Data;
using SIGO.Services;

namespace SIGO.Controllers;

/// <summary>
/// La UI de redeterminación vive en Blazor (Components/Pages/Redeterminacion). Este
/// controller conserva únicamente las descargas de Excel (respuestas de archivo).
/// </summary>
[Microsoft.AspNetCore.Authorization.Authorize(Roles = Roles.Certificaciones)]
public class RedeterminacionController(IDbContextFactory<AppDbContext> dbFactory, RedeterminacionService service) : Controller
{
    // GET: /Redeterminacion/DescargarExcelGuardado/5
    [HttpGet]
    public async Task<IActionResult> DescargarExcelGuardado(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var r = await db.RedeterminacionesGuardadas.AsNoTracking()
            .Include(r => r.Obra)
            .Include(r => r.Items.OrderBy(i => i.Numero))
            .FirstOrDefaultAsync(r => r.Id == id);

        if (r is null) return NotFound();

        return ExcelPonderacion(r.Obra.Nombre,
            r.AnioBase, r.MesBase, r.AnioSalto, r.MesSalto,
            r.Items.Select(i => (i.Numero, i.Insumo, i.PesoPorcentaje, i.ValorMesBase, i.ValorMesSalto, i.KiK0, i.VariacionPonderada, i.DescripcionINDEC)),
            r.TotalKiK0, r.PorcentajeAumento);
    }

    // GET: /Redeterminacion/DescargarExcel?obraId=&tablaPonderacionId=&mesBaseStr=&mesSaltoStr=&idPublicacionBase=&idPublicacionSalto=
    // Base y salto llevan cada uno su publicación: CalcularAsync los modela como
    // independientes y un solo parámetro podía hacer que el Excel calculara con otra
    // combinación que la pantalla (auditoría 2026-07-20, M5).
    [HttpGet]
    public async Task<IActionResult> DescargarExcel(int obraId, int tablaPonderacionId,
        string mesBaseStr, string mesSaltoStr, string? idPublicacionBase, string? idPublicacionSalto)
    {
        if (!Periodo.TryParse(mesBaseStr, out var anioBase, out var mesBase) ||
            !Periodo.TryParse(mesSaltoStr, out var anioSalto, out var mesSalto))
            return BadRequest("Período inválido: se espera el formato AAAA-M.");

        Models.ViewModels.RedeterminacionVM vm;
        try
        {
            vm = await service.CalcularAsync(obraId, tablaPonderacionId,
                anioBase, mesBase, idPublicacionBase,
                anioSalto, mesSalto, idPublicacionSalto);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return BadRequest(ex.Message);
        }

        return ExcelPonderacion(vm.ObraNombre,
            vm.AnioBase, vm.MesBase, vm.AnioSalto, vm.MesSalto,
            vm.Items.Select(i => (i.Numero, i.Insumo, i.PesoPorcentaje, i.ValorMesBase, i.ValorMesSalto, i.KiK0, i.VariacionPonderada, i.DescripcionINDEC)),
            vm.TotalKiK0, vm.PorcentajeAumento);
    }

    /// <summary>"Mes Año" del índice comparado (mes acotado: datos fuera de rango no tiran 500).</summary>
    private static string EtiquetaMes(int mes, int anio) => $"{Fmt.MesNombreSeguro(mes)} {anio}";

    /// <summary>Nombre de obra apto para filename/Content-Disposition (solo letras, dígitos, - y _).</summary>
    private static string SanearNombre(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre)) return "Obra";
        var limpio = new string(nombre.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' ? ch : '_').ToArray());
        return limpio.Trim('_') is { Length: > 0 } s ? s : "Obra";
    }

    /// <summary>
    /// Cierre común de los dos endpoints: etiquetas "Mes Año" (base → salto, igual
    /// que la tabla en pantalla), workbook y nombre de archivo.
    /// </summary>
    private FileContentResult ExcelPonderacion(string? obraNombre,
        int anioBase, int mesBase, int anioSalto, int mesSalto,
        IEnumerable<(int Numero, string Insumo, decimal PesoPorcentaje, decimal? ValorMesBase, decimal? ValorMesSalto, decimal? KiK0, decimal? VariacionPonderada, string? DescripcionINDEC)> items,
        decimal? totalKiK0, decimal? porcentajeAumento)
    {
        var mesBaseLabel = EtiquetaMes(mesBase, anioBase);
        var mesSaltoLabel = EtiquetaMes(mesSalto, anioSalto);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Ponderación");

        ws.Cell(1, 1).Value = $"TABLA DE PONDERACIÓN — {obraNombre}";
        ws.Range(1, 1, 1, 8).Merge().Style
            .Font.SetBold(true).Font.SetFontSize(13)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        ws.Cell(2, 1).Value = $"Período: {mesBaseLabel}  →  {mesSaltoLabel}";
        ws.Range(2, 1, 2, 8).Merge().Style
            .Font.SetItalic(true)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        int headerRow = 4;
        string[] headers = ["N°", "Insumo", "Peso %", $"Indicador\n{mesBaseLabel}", $"Indicador\n{mesSaltoLabel}", "Kᵢ/K₀", "Variación Ponderada", "INDEC"];
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(headerRow, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.SetBold(true)
                .Fill.SetBackgroundColor(XLColor.FromArgb(0x21, 0x21, 0x21))
                .Font.SetFontColor(XLColor.White)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
                .Alignment.SetWrapText(true);
        }

        int row = headerRow + 1;
        foreach (var item in items)
        {
            ws.Cell(row, 1).Value = item.Numero;
            ws.Cell(row, 2).Value = item.Insumo;
            ws.Cell(row, 3).Value = item.PesoPorcentaje / 100m;
            ws.Cell(row, 3).Style.NumberFormat.Format = "0.00%";
            ws.Cell(row, 4).Value = item.ValorMesBase.HasValue ? (XLCellValue)item.ValorMesBase.Value : Blank.Value;
            ws.Cell(row, 4).Style.NumberFormat.Format = ExcelHelpers.FormatoMoneda;
            ws.Cell(row, 5).Value = item.ValorMesSalto.HasValue ? (XLCellValue)item.ValorMesSalto.Value : Blank.Value;
            ws.Cell(row, 5).Style.NumberFormat.Format = ExcelHelpers.FormatoMoneda;
            ws.Cell(row, 6).Value = item.KiK0.HasValue ? (XLCellValue)item.KiK0.Value : Blank.Value;
            ws.Cell(row, 6).Style.NumberFormat.Format = ExcelHelpers.FormatoMoneda;
            ws.Cell(row, 7).Value = item.VariacionPonderada.HasValue ? (XLCellValue)item.VariacionPonderada.Value : Blank.Value;
            ws.Cell(row, 7).Style.NumberFormat.Format = ExcelHelpers.FormatoMoneda;
            ws.Cell(row, 8).Value = item.DescripcionINDEC ?? "";
            ws.Cell(row, 8).Style.Font.SetFontSize(8);
            foreach (int col in Enumerable.Range(1, 8))
                ws.Cell(row, col).Style.Alignment.SetHorizontal(col == 2 || col == 8 ? XLAlignmentHorizontalValues.Left : XLAlignmentHorizontalValues.Center);
            row++;
        }

        var totalRow = row;
        ws.Cell(totalRow, 1).Value = "TOTAL";
        ws.Range(totalRow, 1, totalRow, 2).Merge();
        ws.Cell(totalRow, 3).Value = 1m;
        ws.Cell(totalRow, 3).Style.NumberFormat.Format = "0.00%";
        ws.Cell(totalRow, 6).Value = totalKiK0.HasValue ? (XLCellValue)totalKiK0.Value : Blank.Value;
        ws.Cell(totalRow, 6).Style.NumberFormat.Format = ExcelHelpers.FormatoMoneda;
        ws.Cell(totalRow, 7).Value = porcentajeAumento.HasValue ? (XLCellValue)(porcentajeAumento.Value / 100m) : Blank.Value;
        ws.Cell(totalRow, 7).Style.NumberFormat.Format = "0.00%";
        ws.Range(totalRow, 1, totalRow, 8).Style.Font.SetBold(true)
            .Fill.SetBackgroundColor(XLColor.FromArgb(0x40, 0x40, 0x40))
            .Font.SetFontColor(XLColor.White);

        ws.Range(headerRow, 1, totalRow, 8).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin)
            .Border.SetInsideBorder(XLBorderStyleValues.Thin);
        ws.Column(2).Width = 30;
        ws.Column(8).Width = 40;
        ws.Columns(1, 7).AdjustToContents(headerRow, totalRow);
        ws.Column(2).Width = Math.Max(ws.Column(2).Width, 30);
        ws.Column(8).Width = 40;
        ws.Row(headerRow).Height = 30;

        var fileName = $"Ponderacion_{SanearNombre(obraNombre)}_{anioSalto}-{mesSalto:D2}.xlsx";
        return File(ExcelHelpers.ToBytes(wb), ExcelHelpers.ContentType, fileName);
    }
}
