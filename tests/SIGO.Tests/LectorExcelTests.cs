using ClosedXML.Excel;
using SIGO.Services.Importacion;

namespace SIGO.Tests;

/// <summary>
/// Lectura de planillas con ExcelDataReader: nombres de hoja, numeración original de
/// filas (las vacías no se listan pero cuentan), formato %, errores de Excel y parseo
/// de textos numéricos.
/// Los libros se generan en memoria con ClosedXML, el mismo paquete de los exports.
/// </summary>
public class LectorExcelTests
{
    private static byte[] Libro(Action<XLWorkbook> armar)
    {
        using var wb = new XLWorkbook();
        armar(wb);
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    [Fact]
    public void Leer_DevuelveHojasFilasYFormatos()
    {
        var bytes = Libro(wb =>
        {
            var ws = wb.Worksheets.Add("Datos");
            ws.Cell("A1").Value = "Item";
            ws.Cell("B1").Value = "Descripción";
            ws.Cell("A2").Value = "P.1";
            ws.Cell("B2").Value = "  Texto con espacios  ";
            ws.Cell("C2").Value = 0.1;
            ws.Cell("C2").Style.NumberFormat.Format = "0.00%";
            ws.Cell("D2").Value = 1234.5;
            ws.Cell("E2").Value = "1.234,56";
            // Fila 3 vacía a propósito; la 4 tiene datos.
            ws.Cell("A4").Value = "X";
            wb.Worksheets.Add("Vacía");
        });

        var libro = LectorExcel.Leer(bytes);

        Assert.Equal(["Datos", "Vacía"], libro.Hojas.Select(h => h.Nombre));
        var hoja = libro.Hojas[0];
        Assert.Equal([1, 2, 4], hoja.Filas.Select(f => f.Numero));

        var fila2 = hoja.Filas[1];
        Assert.Equal("P.1", fila2[0].Texto);
        Assert.Equal("Texto con espacios", fila2[1].Texto);
        Assert.True(fila2[2].EsPorcentaje);
        Assert.Equal(0.1m, fila2[2].Numero);
        Assert.False(fila2[3].EsPorcentaje);
        Assert.Equal(1234.5m, fila2[3].Numero);
        Assert.Equal(1234.56m, fila2[4].Numero);   // texto es-AR
        Assert.True(fila2[9].EstaVacia);           // fuera de rango → vacía, no excepción
        Assert.Empty(libro.Hojas[1].Filas);
    }

    [Fact]
    public void Leer_TextoEnCeldaConFormatoPorcentaje_NoEsFraccion()
    {
        // «0.5%» con punto: Excel es-AR lo deja como texto aunque la celda tenga formato %.
        // El lector conserva el formato, así que la regla de fracción no puede mirar solo eso.
        var bytes = Libro(wb =>
        {
            var ws = wb.Worksheets.Add("Datos");
            ws.Cell("A1").Value = "0.5%";
            ws.Cell("A1").Style.NumberFormat.Format = "0.00%";
            ws.Cell("B1").Value = 0.005;
            ws.Cell("B1").Style.NumberFormat.Format = "0.00%";
        });

        var fila = LectorExcel.Leer(bytes).Hojas[0].Filas[0];

        Assert.True(fila[0].EsTexto);
        Assert.False(fila[0].EsPorcentaje);
        Assert.Equal(0.5m, fila[0].Numero);
        Assert.True(fila[1].EsPorcentaje);
        Assert.Equal(0.005m, fila[1].Numero);
    }

    [Fact]
    public void Leer_MarcaLasHojasOcultas()
    {
        // Agüero, certificados N°15 y N°16: «CERTIFICADO» está oculta y es una copia del N°14.
        var bytes = Libro(wb =>
        {
            wb.Worksheets.Add("CERTIFICADO").Cell("A1").Value = "copia vieja";
            wb.Worksheets.Add("CE B").Cell("A1").Value = "vigente";
            wb.Worksheet("CERTIFICADO").Hide();
        });

        var libro = LectorExcel.Leer(bytes);

        Assert.True(libro.Hojas.Single(h => h.Nombre == "CERTIFICADO").Oculta);
        Assert.False(libro.Hojas.Single(h => h.Nombre == "CE B").Oculta);
    }

    [Fact]
    public void Leer_ErroresDeExcel_NoSonCeldasVaciasNiTextoNiNumero()
    {
        var bytes = Libro(wb =>
        {
            var ws = wb.Worksheets.Add("Datos");
            ws.Cell("A1").Value = XLError.DivisionByZero;
            ws.Cell("B1").Value = XLError.CellReference;
            ws.Cell("C1").Value = 1;
        });

        var fila = LectorExcel.Leer(bytes).Hojas[0].Filas[0];

        Assert.Equal("#DIV/0!", fila[0].Error);
        Assert.Equal("#REF!", fila[1].Error);
        Assert.Equal("", fila[0].Texto);           // «#DIV/0!» tiene un dígito: como texto pasaría por código
        Assert.Null(fila[0].Numero);
        Assert.False(fila[0].EstaVacia);           // un #REF! en el % del mes no es 0 %
        Assert.Null(fila[2].Error);
    }

    [Fact]
    public void Leer_DesdeUnStream_LeeDesdeElPrincipio()
    {
        // El selector le pasa la copia del archivo subido tal como quedó: con la posición al final.
        var bytes = Libro(wb => wb.Worksheets.Add("Datos").Cell("A1").Value = "Item");
        using var copia = new MemoryStream(bytes);
        copia.Position = copia.Length;

        Assert.Equal("Item", LectorExcel.Leer(copia).Hojas[0].Filas[0][0].Texto);
    }

    [Fact]
    public void Leer_ArchivoQueNoEsPlanilla_LanzaMensajeParaElUsuario()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => LectorExcel.Leer("esto no es un excel"u8.ToArray()));
        Assert.Contains("planilla", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<InvalidOperationException>(() => LectorExcel.Leer([]));
    }

    [Fact]
    public void Leer_XlsxTruncado_TambienEsMensajeParaElUsuario()
    {
        // Firma zip («PK») seguida de basura: el ZipArchive falla con una excepción que no
        // deriva de ExcelReaderException y que igual debe llegar al selector como
        // InvalidOperationException (lo único que atrapa con el mensaje del lector).
        var bytes = new byte[] { 0x50, 0x4B, 0x03, 0x04 }.Concat(Enumerable.Range(0, 200).Select(i => (byte)(i * 7))).ToArray();
        var ex = Assert.Throws<InvalidOperationException>(() => LectorExcel.Leer(bytes));
        Assert.Contains("No se pudo leer", ex.Message);
    }

    [Theory]
    [InlineData(1.1, "0.00", "1.10")]          // código numérico con decimales fijos
    [InlineData(1.0, null, "1")]
    [InlineData(1234.5, "#,##0.00", "1234.50")]
    [InlineData(2.0, "General", "2")]
    [InlineData(0.1, "0.00%", "0.10")]
    public void Texto_DeNumero_RespetaLosDecimalesFijosDelFormato(double valor, string? formato, string esperado) =>
        Assert.Equal(esperado, new CeldaExcel(valor, formato).Texto);

    [Theory]
    [InlineData("0.00%", true)]
    [InlineData("0%", true)]
    [InlineData("0.00\"%\"", false)]      // % entre comillas: Excel lo muestra sin multiplicar por 100
    [InlineData("0.00\\%", false)]        // % escapado: ídem
    [InlineData("#,##0.00", false)]
    [InlineData(null, false)]
    public void EsPorcentaje_SoloConUnPorcentajeQueEscala(string? formato, bool esperado) =>
        Assert.Equal(esperado, new CeldaExcel(0.1, formato).EsPorcentaje);

    [Theory]
    [InlineData("oferta.xls", true)]
    [InlineData("oferta.XLSX", true)]
    [InlineData("oferta.xlsm", true)]
    [InlineData("oferta.csv", false)]
    [InlineData("oferta", false)]
    [InlineData(null, false)]
    public void ValidarArchivo_SoloExtensionesExcel(string? nombre, bool admitido) =>
        Assert.Equal(admitido, LectorExcel.ValidarArchivo(nombre, 1024) is null);

    [Fact]
    public void ValidarArchivo_RechazaMasDe12MbConElTamanoReal()
    {
        Assert.Null(LectorExcel.ValidarArchivo("oferta.xlsx", LectorExcel.TamanoMaximoBytes));

        var mensaje = LectorExcel.ValidarArchivo("ALGIERI - OFERTA.xlsx", 14_155_776);   // 13,5 MB

        Assert.Contains("pesa 13,5 MB y el máximo es 12 MB", mensaje);
        Assert.Contains("copia solo con las hojas", mensaje);

        // Apenas pasado el máximo, el tamaño se redondea hacia arriba: «pesa 12 MB y el máximo
        // es 12 MB» no explicaría el rechazo.
        Assert.Contains("pesa 12,1 MB", LectorExcel.ValidarArchivo("oferta.xlsx", LectorExcel.TamanoMaximoBytes + 40_000));
    }

    [Theory]
    [InlineData(300, "1 KB")]
    [InlineData(870_400, "850 KB")]
    [InlineData(2_621_440, "2,5 MB")]
    [InlineData(12 * 1024 * 1024, "12 MB")]
    public void DescribirTamano_KbOMbEnCastellano(long bytes, string esperado) =>
        Assert.Equal(esperado, LectorExcel.DescribirTamano(bytes));

    [Theory]
    [InlineData("Precio    Unitario", "precio unitario")]
    [InlineData("  Descripción ", "descripcion")]
    [InlineData("ÍTEM", "item")]
    [InlineData(null, "")]
    public void Normalizar_SinAcentosNiMayusculasNiEspaciosDobles(string? texto, string esperado) =>
        Assert.Equal(esperado, TextoImport.Normalizar(texto));

    [Theory]
    [InlineData("PEV-SD-EAG-01.1.1", true)]
    [InlineData("ELV.2", true)]
    [InlineData("01.1", true)]
    [InlineData("PEV-SD-EAG-AD 01.1", true)]              // un espacio, corto, mayúsculas
    [InlineData("Interferencia Nº 6 - Red Cloacal", false)]
    [InlineData("TOTAL ESTACION LACROZE - ASCENSOR N°1 - C/IVA", false)]
    [InlineData("TOTAL RUBRO 1", false)]                 // corto y en mayúsculas, pero es un total
    [InlineData("SUBTOTAL 01.1", false)]
    [InlineData("GENERALES DE PROYECTO", false)]
    [InlineData("-", false)]
    [InlineData("", false)]
    public void PareceCodigo_DistingueCodigosDeTitulos(string texto, bool esperado) =>
        Assert.Equal(esperado, TextoImport.PareceCodigo(texto));

    [Theory]
    [InlineData("12,5%", 12.5)]
    [InlineData("10 %", 10.0)]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("s/d", null)]
    public void Numero_DeTexto_AdmiteSignoDePorcentaje(string texto, double? esperado) =>
        Assert.Equal(esperado is null ? null : (decimal)esperado.Value, new CeldaExcel(texto, null).Numero);

    [Fact]
    public void ClaveCodigo_IgnoraEspaciosYMayusculas() =>
        Assert.Equal(TextoImport.ClaveCodigo(" pev-sd-eag-01.1 "), TextoImport.ClaveCodigo("PEV-SD-EAG-01.1"));

    [Fact]
    public void EtiquetaHoja_DetalleYMarcasDeOcultaYRecortada()
    {
        Assert.Equal("CE B", TextoImport.EtiquetaHoja(new HojaExcel("CE B", []), null));
        Assert.Equal("CE B — N°15 jul/2026 · oculta", TextoImport.EtiquetaHoja(new HojaExcel("CE B", [], oculta: true), "N°15 jul/2026"));
        Assert.Equal("Larga · recortada a 50.000 filas", TextoImport.EtiquetaHoja(new HojaExcel("Larga", [], recortada: true), null));
    }
}
