using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SIGO.Controllers;
using SIGO.Services;
using SIGO.Services.Importacion;

namespace SIGO.Tests;

/// <summary>
/// Plantilla de índices contra LocalDB: la descarga trae el catálogo vigente y, completada
/// con el pegado del PDF, entra por el mismo análisis e importación que el pegado de texto.
/// </summary>
public class PlantillaIndicesImportacionTests(LocalDbFixture fx) : IClassFixture<LocalDbFixture>
{
    private IndiceService Servicio() => new(new TestDbFactory(fx.Options), new FakeCurrentUser(Roles.CCyR));

    [Fact]
    public async Task Descargar_ConPublicacionInvalida_BadRequest()
    {
        var resultado = await new IndicesController(Servicio()).DescargarPlantilla("INDEC_INFORMA_7_26");

        Assert.IsType<BadRequestObjectResult>(resultado);
    }

    [Fact]
    public async Task DescargarPegarYSubir_ImportaLosValoresConLaPublicacionDelArchivo()
    {
        const string pub = "INDEC_INFORMA_03_89";   // propia del test: meses feb-88 … feb-89
        var servicio = Servicio();
        var descarga = Assert.IsType<FileContentResult>(await new IndicesController(servicio).DescargarPlantilla($" {pub} "));
        Assert.Equal($"Plantilla_indices_{pub}.xlsx", descarga.FileDownloadName);

        byte[] completada;
        using (var wb = new XLWorkbook(new MemoryStream(descarga.FileContents)))
        {
            var ws = wb.Worksheet(PlantillaIndices.HojaCarga);
            // Todo el catálogo vigente salvo las tasas del Banco Nación, que van sin publicación.
            var codigos = ws.Column(1).CellsUsed().Select(c => c.GetString()).ToHashSet();
            Assert.All((await servicio.ListarAsync()).Where(i => !IndiceService.EsTasasBancoNacion(i.CodigoIndice)),
                i => Assert.Contains(i.CodigoIndice, codigos));
            Assert.DoesNotContain(IndiceService.CodigoTasasBancoNacion, codigos);

            // La fila del PDF pegada en «Pegado del PDF» (columna 6) de MANO_OBRA.
            var fila = ws.Column(1).CellsUsed().First(c => c.GetString() == "MANO_OBRA").Address.RowNumber;
            ws.Cell(fila, 6).Value = "Mano de obra 100,0 " + string.Join("  ", Enumerable.Range(1, 13).Select(i => $"{500 + i},5"));
            using var salida = new MemoryStream();
            wb.SaveAs(salida);
            completada = salida.ToArray();
        }

        var lectura = PlantillaIndices.Leer(LectorExcel.Leer(completada));
        Assert.Equal(pub, lectura.IdPublicacion);
        await servicio.AnalizarImportacionAsync(lectura.Filas, lectura.IdPublicacion);
        Assert.Equal(13, await servicio.ImportarAsync(lectura.Filas, lectura.IdPublicacion));

        await using var db = fx.CrearContexto();
        var guardados = await db.ValoresIndice
            .Where(v => v.IdPublicacion == pub)
            .OrderBy(v => v.Anio).ThenBy(v => v.Mes)
            .Select(v => new { v.Indice.CodigoIndice, v.Anio, v.Mes, v.Valor })
            .ToListAsync();
        Assert.Equal(13, guardados.Count);
        Assert.All(guardados, v => Assert.Equal("MANO_OBRA", v.CodigoIndice));
        Assert.Equal((2088, 2, 501.5m), (guardados[0].Anio, guardados[0].Mes, guardados[0].Valor));
        Assert.Equal((2089, 2, 513.5m), (guardados[^1].Anio, guardados[^1].Mes, guardados[^1].Valor));
    }
}
