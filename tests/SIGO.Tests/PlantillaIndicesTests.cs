using ClosedXML.Excel;
using SIGO.Models.ViewModels;
using SIGO.Services;
using SIGO.Services.Importacion;

namespace SIGO.Tests;

/// <summary>
/// Plantilla Excel de valores mensuales: la fila de valores pegada del PDF se reparte en los
/// meses como en el Excel maestro, y la lectura da las mismas filas que el pegado de texto.
/// Los libros se completan con ClosedXML, que no recalcula: las celdas de mes quedan sin valor
/// y manda el reparto en C#. El archivo guardado por Excel real cubre el otro camino (las
/// fórmulas ya calculadas).
/// </summary>
public class PlantillaIndicesTests
{
    private const string Publicacion = "INDEC_INFORMA_07_26";
    private static readonly DateTime Jun25 = new(2025, 6, 1), Sep25 = new(2025, 9, 1), Jun26 = new(2026, 6, 1);

    // Filas del anexo de la revista 07/26 (las del Excel maestro provisorio).
    private const string FilaAlbanileria =
        "9.586,3  9.796,6  9.974,6  10.287,4  10.420,6  10.820,1  10.976,4  11.318,9  11.542,6  11.819,6  12.319,1  12.724,8  13.074,3";
    private static readonly decimal[] ValoresAlbanileria =
        [9586.3m, 9796.6m, 9974.6m, 10287.4m, 10420.6m, 10820.1m, 10976.4m, 11318.9m, 11542.6m, 11819.6m, 12319.1m, 12724.8m, 13074.3m];

    private static readonly List<IndiceVM> Catalogo =
    [
        new() { CodigoIndice = "MANO_OBRA", FamiliaRecurso = "Mano de obra", IndiceNormalizado = "a) Mano de obra", CuadroReferencia = "Cuadro 1.4", IncisoCode = "a" },
        new() { CodigoIndice = "ALBANILERIA", FamiliaRecurso = "Albañilería" }
    ];

    /// <summary>La hoja Carga editada como lo haría el usuario: pegado por índice y celdas de mes tipeadas.</summary>
    private sealed class Carga(IXLWorksheet ws)
    {
        public IXLWorksheet Hoja => ws;
        public int FilaEncabezado => Encabezado.RowNumber();
        private IXLRow Encabezado => ws.RowsUsed().First(r => r.Cell(1).GetString() == "CÓDIGO");
        public int Fila(string codigo) => ws.RowsUsed().First(r => r.Cell(1).GetString() == codigo).RowNumber();
        public int Col(DateTime mes) => Encabezado.CellsUsed().First(c => c.Value.IsDateTime && c.GetDateTime() == mes).Address.ColumnNumber;
        private int ColPegado => Encabezado.CellsUsed().First(c => c.GetString().StartsWith("Pegado")).Address.ColumnNumber;
        public void Pegar(string codigo, string texto) => ws.Cell(Fila(codigo), ColPegado).Value = texto;
        public void Tipear(string codigo, DateTime mes, XLCellValue valor) => ws.Cell(Fila(codigo), Col(mes)).Value = valor;
    }

    private static byte[] Completar(Action<Carga> completar, Action<XLWorkbook>? libro = null)
    {
        using var wb = new XLWorkbook(new MemoryStream(PlantillaIndices.Crear(Publicacion, Catalogo)));
        completar(new Carga(wb.Worksheet(PlantillaIndices.HojaCarga)));
        libro?.Invoke(wb);
        using var salida = new MemoryStream();
        wb.SaveAs(salida);
        return salida.ToArray();
    }

    private static LecturaPlantillaIndices Leer(byte[] archivo) => PlantillaIndices.Leer(LectorExcel.Leer(archivo));

    // ── Meses y creación ─────────────────────────────────────────────────────────

    [Fact]
    public void Meses_SonLosTreceQueMuestraLaRevista()
    {
        var julio = PlantillaIndices.Meses(2026, 7);
        Assert.Equal(13, julio.Count);
        Assert.Equal(Jun25, julio[0]);
        Assert.Equal(Jun26, julio[^1]);

        // La de enero termina en diciembre del año anterior.
        Assert.Equal(new DateTime(2026, 12, 1), PlantillaIndices.Meses(2027, 1)[^1]);
    }

    [Fact]
    public void Crear_ConPublicacionInvalida_Rechaza() =>
        Assert.Throws<InvalidOperationException>(() => PlantillaIndices.Crear("07_26", Catalogo));

    [Fact]
    public void Crear_NoIncluyeLasTasasDelBancoNacion()
    {
        // Se cargan sin publicación: con la de la plantilla le ganarían a la tasa en Calcular.
        List<IndiceVM> catalogo = [.. Catalogo, new() { CodigoIndice = "COSTO_FINANCIERO", FamiliaRecurso = "Costo Financiero" }];

        using var wb = new XLWorkbook(new MemoryStream(PlantillaIndices.Crear(Publicacion, catalogo)));
        var codigos = wb.Worksheet(PlantillaIndices.HojaCarga).Column(1).CellsUsed().Select(c => c.GetString()).ToList();

        Assert.Contains("MANO_OBRA", codigos);
        Assert.DoesNotContain("COSTO_FINANCIERO", codigos);
    }

    [Fact]
    public void TasasDelBancoNacionAgregadasAMano_NoSeImportan()
    {
        var filas = Leer(Completar(c =>
        {
            var fila = c.Fila("ALBANILERIA") + 1;
            c.Hoja.Cell(fila, 1).Value = "costo_financiero";
            c.Hoja.Cell(fila, 6).Value = FilaAlbanileria;
        })).Filas;

        var f = Assert.Single(filas);
        Assert.Equal(EstadoFilaImport.FilaInvalida, f.Estado);
        Assert.Contains("sin publicación", f.Detalle);
    }

    [Fact]
    public void SinCompletar_TraeLaPublicacionYNingunValor()
    {
        var lectura = Leer(PlantillaIndices.Crear(Publicacion, Catalogo));

        Assert.Null(lectura.Error);
        Assert.Equal(Publicacion, lectura.IdPublicacion);
        Assert.Empty(lectura.Filas);
    }

    // ── Reparto del pegado ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(FilaAlbanileria)]
    [InlineData("Albañilería (Cuadro 1.5) 100,0 " + FilaAlbanileria)]                  // fila entera: se descarta hasta la base
    [InlineData("9.586,3\t9.796,6 9.974,6\n10.287,4 10.420,6 10.820,1 10.976,4 11.318,9 11.542,6 11.819,6 12.319,1 12.724,8 13.074,3")]
    public void RepartirPegado_UnValorPorMes(string pegado)
    {
        var (valores, error) = PlantillaIndices.RepartirPegado(pegado, 13);

        Assert.Null(error);
        Assert.Equal(ValoresAlbanileria.Select(v => (decimal?)v), valores);
    }

    [Fact]
    public void RepartirPegado_LaSDejaElMesVacio()
    {
        var (valores, error) = PlantillaIndices.RepartirPegado("1,0 2,0 s 4,0", 4);

        Assert.Null(error);
        Assert.Equal([1.0m, 2.0m, null, 4.0m], valores);
    }

    [Theory]
    [InlineData("1,0 2,0 3,0", "3 valores")]                                          // de menos: caerían en el mes equivocado
    [InlineData("1,0 2,0 3,0 4,0 5,0", "5 valores")]
    [InlineData("Mano 2,0 3,0 4,0", "«Mano»")]                                       // la descripción sin la base
    public void RepartirPegado_QueNoSePuedeRepartir_DiceElMotivo(string pegado, string motivo)
    {
        var (valores, error) = PlantillaIndices.RepartirPegado(pegado, 4);

        Assert.Empty(valores);
        Assert.Contains(motivo, error);
    }

    // ── Lectura ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Pegado_SeRepartePorMesConElPeriodoDeCadaColumna()
    {
        var filas = Leer(Completar(c => c.Pegar("ALBANILERIA", FilaAlbanileria))).Filas;

        Assert.Equal(13, filas.Count);
        Assert.All(filas, f => Assert.Equal((EstadoFilaImport.Ok, "ALBANILERIA"), (f.Estado, f.Codigo)));
        Assert.Equal(PlantillaIndices.Meses(2026, 7).Select(m => (m.Year, m.Month)), filas.Select(f => (f.Anio, f.Mes)));
        Assert.Equal(ValoresAlbanileria.Select(v => (decimal?)v), filas.Select(f => f.Valor));
    }

    [Fact]
    public void MesTipeado_TienePrioridadSobreElPegado()
    {
        var filas = Leer(Completar(c =>
        {
            c.Pegar("ALBANILERIA", FilaAlbanileria);
            c.Tipear("ALBANILERIA", Jun26, 13100);
            c.Tipear("MANO_OBRA", Sep25, 9591.7);       // sin pegado: solo ese mes
        })).Filas;

        Assert.Equal(13100m, filas.Single(f => f.Codigo == "ALBANILERIA" && f.Anio == 2026 && f.Mes == 6).Valor);
        Assert.Equal(9586.3m, filas.Single(f => f.Codigo == "ALBANILERIA" && f.Anio == 2025 && f.Mes == 6).Valor);
        var manoDeObra = Assert.Single(filas, f => f.Codigo == "MANO_OBRA");
        Assert.Equal((2025, 9, 9591.7m), (manoDeObra.Anio, manoDeObra.Mes, manoDeObra.Valor));
    }

    [Fact]
    public void PegadoQueNoSePuedeRepartir_EsUnaFilaInvalidaConElMotivo()
    {
        var filas = Leer(Completar(c =>
        {
            c.Pegar("ALBANILERIA", "12.746,9  12.801,0  13.030,3");
            c.Tipear("ALBANILERIA", Jun26, 13100);      // con el pegado mal, tampoco se toma
        })).Filas;

        var f = Assert.Single(filas);
        Assert.Equal(EstadoFilaImport.FilaInvalida, f.Estado);
        Assert.Contains("3 valores", f.Detalle);
    }

    [Fact]
    public void MesesTipeadosQueNoSirven_SonFilaInvalidaConElMotivo()
    {
        var filas = Leer(Completar(c =>
        {
            c.Tipear("MANO_OBRA", Jun26, "s");
            c.Tipear("MANO_OBRA", Jun25, 0);
            c.Tipear("ALBANILERIA", Jun26, XLError.DivisionByZero);
        })).Filas;

        Assert.Equal(3, filas.Count);
        Assert.All(filas, f => Assert.Equal(EstadoFilaImport.FilaInvalida, f.Estado));
        Assert.Contains(filas, f => f.Detalle == "«s» no es un número.");
        Assert.Contains(filas, f => f.Detalle == "El valor debe ser mayor que cero.");
        Assert.Contains(filas, f => f.Detalle == "La celda tiene un error de Excel (#DIV/0!).");
    }

    [Fact]
    public void FilaConValoresSinCodigo_NoSePierdeEnSilencio()
    {
        var filas = Leer(Completar(c =>
        {
            c.Pegar("MANO_OBRA", FilaAlbanileria);
            c.Hoja.Cell(c.Fila("MANO_OBRA"), 1).Value = Blank.Value;
        })).Filas;

        var f = Assert.Single(filas);
        Assert.Equal(EstadoFilaImport.FilaInvalida, f.Estado);
        Assert.Contains("no tiene código", f.Detalle);
    }

    [Fact]
    public void ConFilasYColumnasAgregadas_UbicaTodoPorRotulos()
    {
        var lectura = Leer(Completar(c =>
        {
            c.Pegar("ALBANILERIA", FilaAlbanileria);
            c.Hoja.Column(1).InsertColumnsBefore(1);
            c.Hoja.Row(1).InsertRowsAbove(2);
        }));

        Assert.Equal(Publicacion, lectura.IdPublicacion);
        Assert.Equal(13, lectura.Filas.Count(f => f.Estado == EstadoFilaImport.Ok));
    }

    // ── Archivos que no se pueden usar ───────────────────────────────────────────────

    [Theory]
    [InlineData("sin hoja Carga", "no tiene la hoja")]
    [InlineData("sin ID", "no tiene el ID de publicación")]
    [InlineData("ID inválido", "Corregilo en la celda")]
    [InlineData("mes repetido", "está en dos columnas")]
    [InlineData("fecha borrada", "encabezado no es un mes")]
    [InlineData("segundo encabezado", "segundo encabezado")]
    [InlineData("otra versión", "no es la plantilla")]
    public void Rechaza_ConElMotivo(string caso, string motivo)
    {
        var archivo = Completar(c =>
        {
            var encabezado = c.FilaEncabezado;
            switch (caso)
            {
                case "sin hoja Carga": c.Hoja.Name = "Otra"; break;
                case "sin ID": c.Hoja.Cell(1, 2).Value = Blank.Value; break;
                case "ID inválido": c.Hoja.Cell(1, 2).Value = "INDEC_INFORMA_7_26"; break;
                case "mes repetido": c.Hoja.Cell(encabezado, c.Col(Jun26)).Value = Jun25; break;
                case "fecha borrada":
                    c.Tipear("MANO_OBRA", Jun26, 12401.3);
                    c.Hoja.Cell(encabezado, c.Col(Jun26)).Value = Blank.Value;
                    break;
                case "segundo encabezado": c.Hoja.Cell(c.Fila("ALBANILERIA"), 1).Value = "CÓDIGO"; break;
            }
        }, wb =>
        {
            if (caso == "otra versión") wb.Worksheet("SIGO").Cell(1, 2).Value = 2;
        });

        var lectura = Leer(archivo);

        Assert.Contains(motivo, lectura.Error);
        Assert.Empty(lectura.Filas);
    }

    [Fact]
    public void UnExcelQueNoEsLaPlantilla_Rechaza()
    {
        // Mismo aspecto (hoja Carga, columna CÓDIGO, meses) pero sin la marca de la app.
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(PlantillaIndices.HojaCarga);
        ws.Cell(1, 1).Value = "CÓDIGO";
        ws.Cell(1, 2).Value = Jun26;
        ws.Cell(2, 1).Value = "MANO_OBRA";
        ws.Cell(2, 2).Value = 12401.3;
        using var salida = new MemoryStream();
        wb.SaveAs(salida);

        Assert.Contains("no es la plantilla", Leer(salida.ToArray()).Error);
    }

    // ── Archivo guardado por Excel real ──────────────────────────────────────────────

    [Fact]
    public void GuardadaPorExcel_TomaLosValoresQueCalcularonLasFormulas()
    {
        // Plantilla generada por la app con el catálogo semilla, completada y guardada con
        // Excel en castellano: ALBANILERIA pegada y jun-26 tipeado (13.100), MANO_OBRA con la
        // fila entera y la base, CAMIONES_CHASIS con tabulación, espacio duro y una «s» en
        // sep-25, PVC con 3 valores y CEMENTO_CAL con uno solo.
        var lectura = Leer(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Datos", "plantilla-indices-guardada-en-excel.xlsx")));

        Assert.Null(lectura.Error);
        Assert.Equal(Publicacion, lectura.IdPublicacion);
        var albanileria = lectura.Filas.Where(f => f.Codigo == "ALBANILERIA").ToList();
        Assert.Equal(ValoresAlbanileria[..12].Append(13100m), albanileria.Select(f => f.Valor!.Value));
        Assert.Equal(13, lectura.Filas.Count(f => f.Codigo == "MANO_OBRA" && f.Estado == EstadoFilaImport.Ok));
        Assert.Equal(12401.3m, lectura.Filas.Single(f => f.Codigo == "MANO_OBRA" && f.Anio == 2026 && f.Mes == 6).Valor);
        var camiones = lectura.Filas.Where(f => f.Codigo == "CAMIONES_CHASIS").ToList();
        Assert.Equal(12, camiones.Count);
        Assert.DoesNotContain(camiones, f => f.Anio == 2025 && f.Mes == 9);
        Assert.Contains("3 valores", Assert.Single(lectura.Filas, f => f.Codigo == "PVC").Detalle);
        Assert.Contains("1 valor ", Assert.Single(lectura.Filas, f => f.Codigo == "CEMENTO_CAL").Detalle);
        Assert.Equal(40, lectura.Filas.Count);
    }
}
