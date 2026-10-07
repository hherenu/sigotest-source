using SIGO.Models.ViewModels;
using SIGO.Services.Importacion;
using static SIGO.Tests.HojaExcelBuilder;

namespace SIGO.Tests;

/// <summary>
/// Parser de la hoja CERTIFICADO de CCyR y matching contra los bloques: encabezado de
/// una o dos filas, cabecera «CERTIFICADO N°… - período», porcentajes como fracción con
/// formato %, secciones por fila TOTAL (con o sin encabezado propio) y resumen final ignorado.
/// </summary>
public class ImportadorCertificadoTests
{
    [Theory]
    [InlineData("CERTIFICADO N°01 - MAY/25", 1, 5, 2025)]
    [InlineData("CERTIFICADO N°14 - JUNIO/26", 14, 6, 2026)]
    [InlineData("CERTIFICADO N°14 - JUNIO DE 2026", 14, 6, 2026)]
    [InlineData("CERTIFICADO DE OBRA N°20 - AGOSTO 2026", 20, 8, 2026)]
    [InlineData("CERTIFICADO N°01 - JUN/26", 1, 6, 2026)]
    [InlineData("Certificado Nº 7 - SET/25", 7, 9, 2025)]
    [InlineData("CERTIFICADO Nro 7 - SET/25", 7, 9, 2025)]
    [InlineData("CERTIFICADO Nro. 12 - DIC/25", 12, 12, 2025)]
    [InlineData("CERTIFICADO N.° 3 - MAR 2026", 3, 3, 2026)]
    [InlineData("LP 243/24 - PUESTA EN VALOR - RENGLÓN 1", null, null, null)]
    public void ParsearEncabezado_NumeroMesYAnio(string texto, int? numero, int? mes, int? anio) =>
        Assert.Equal((numero, mes, anio), ImportadorCertificado.ParsearEncabezado(texto));

    // Hoja de Agüero: arranca en la columna B, encabezado de dos filas, TOTAL en la
    // columna del precio unitario y resumen de anticipo en la columna H.
    private static HojaExcel HojaAguero() => Hoja(
        Blanco,
        [null, "LP 243/24  - “PUESTA EN VALOR ESTACIÓN AGUERO”", null, null, "FECHA CONTRATO", 45737],
        [null, "CONTRATISTA: LIHUE INGENIERIA S.A."],
        [null, "FECHA OFERTA (S 1):", 45630],
        Blanco,
        [null, "CERTIFICADO N°01 - MAY/25"],
        EncabezadoCertificado,
        SubEncabezadoCertificado,
        [null, "PEV-GE.01", "GENERALES DE PROYECTO"],
        [null, "PEV-GE.01.1", "Obrador", "Gl", 1, 13273219.03, 13273219.03, null, null, Pct(0), 0, 0, 0],
        [null, "PEV-GE.01.3", "Replanteo y nivelación", "Gl", 1, 2584417.15, 2584417.15, Pct(0.2), Pct(0.1), Pct(0.3), 516883.43, 258441.72, 775325.15],
        [null, "PEV-SD-EAG-01.1.3", "Desmontaje de troneras (No Aplica)"],
        [null, "PEV-SD-EAG-01.1.10", "Limpieza profunda (No aplica)", "gl"],
        [null, "PEV-SD-EAG-01.18", "Instalación Eléctrica", null, null, null, null, null, Pct(0), Pct(0), 0, 0, 0],
        [null, null, "Desmontaje de bandejas existentes"],
        [null, "PEV-SD-EAG-01.18.1", "Desmontaje de bandejas 50mm", "m", 300, 9001.52, 2700456, null, Pct(0.5), Pct(0.5), 0, 1350228, 1350228],
        [null, null, null, null, null, "TOTAL LÍNEA D - ESTACIÓN AGÜERO=", 4025136349.28, Pct(0), Pct(0.0085), Pct(0.0085), 0, 1608669.72, 1608669.72],
        [null, null, null, null, null, "S/LIHUE", 4025136349.28, 0, 0.0085, 0.0085, null, 1608669.7, 1608669.7],
        [null, null, null, null, null, "DIFERENCIAS", 0, 0, 0, 0, 0, 0.02, 0.02],
        Blanco,
        [null, null, null, null, null, null, null, "CERTIFICADO N°01 - MAY/25", null, null, null, "S/ CCYR", "DIFERENCIA"],
        [null, null, null, null, null, null, null, "CERTIFICADO de OBRA a Precios Básicos", null, null, 1608669.7, 1608669.72, 0.02],
        [null, null, null, null, null, null, null, "TOTAL CERTIFICADO NETO DE ANTICIPO", null, null, 1286935.76]);

    [Fact]
    public void Aguero_CabeceraSeccionYPorcentajesEnPuntos()
    {
        var a = ImportadorCertificado.Analizar(HojaAguero());

        Assert.Null(a.Error);
        Assert.Equal("CERTIFICADO N°01 - MAY/25", a.Encabezado);
        Assert.Equal((1, 5, 2025), (a.Numero, a.Mes, a.Anio));
        Assert.True(a.CoincideCon(1, 5, 2025));
        Assert.False(a.CoincideCon(1, 6, 2025));

        var s = Assert.Single(a.Secciones);
        Assert.Equal("Sección 1", s.Titulo);        // la fila anterior al encabezado es la cabecera, no un título
        Assert.Equal(1608669.72m, s.TotalMontoActualPlanilla);

        // Los rubros codificados (sin valores, o con ceros residuales en las columnas de %)
        // y el «No Aplica» sin unidad no se listan; el «No aplica» con unidad sí (vale 0 %).
        Assert.Equal(["PEV-GE.01.1", "PEV-GE.01.3", "PEV-SD-EAG-01.1.10", "PEV-SD-EAG-01.18.1"], s.Filas.Select(f => f.Codigo));

        var replanteo = s.Filas[1];
        Assert.Equal(10m, replanteo.PctActual);     // 0,1 con formato % → 10 puntos
        Assert.Equal(20m, replanteo.PctAnterior);
        Assert.Equal(258441.72m, replanteo.MontoActualPlanilla);
        Assert.Null(s.Filas[2].PctActual);
        Assert.Equal(2, s.ConPorcentaje);
    }

    [Fact]
    public void DosSeccionesConTituloYTotalGeneralIgnorado()
    {
        // Lacroze: dos estaciones en la misma hoja, cada una con título, encabezado y TOTAL
        // en la columna del código; después un total general y el resumen en la columna H.
        var hoja = Hoja(
            [null, "CERTIFICADO N°01 - JUN/26"],
            [null, "LINEA B - ESTACION LACROZE"],
            EncabezadoCertificado,
            SubEncabezadoCertificado,
            [null, "ELV.1.1", "Obrador", "gl", 0.4, 11808220.82, 4723288.33, null, Pct(0.48), Pct(0.48), 0, 2267178.4, 2267178.4],
            [null, "TOTAL ESTACION LACROZE - ASCENSOR N°1 - C/IVA", null, null, null, null, 469891546.36, null, Pct(0.1125), Pct(0.1125), 0, 52883392.14, 52883392.14],
            [null, null, null, null, null, "S/FEMYP", 469891546.37],
            Blanco,
            [null, "LINEA E - ESTACION VIRREYES"],
            EncabezadoCertificado,
            SubEncabezadoCertificado,
            [null, "ELV.1.1", "Obrador", "gl", 0.6, 11808220.82, 7084932.49, null, Pct(0.48), Pct(0.48), 0, 3400767.6, 3400767.6],
            [null, "TOTAL ESTACION PLAZA DE LOS VIRREYES - ASCENSOR N°1 - C/IVA", null, null, null, null, 692133430.85, null, Pct(0.0118), Pct(0.0118), 0, 8141726.84, 8141726.84],
            Blanco,
            [null, "TOTAL ELV.1 + ESTACION LACROZE + ESTACION VIRREYES C/IVA", null, null, null, null, 1162024977.21, null, Pct(0.05), Pct(0.05), 0, 61025118.98, 61025118.98],
            [null, null, null, null, null, null, null, "ESTACION LACROZE"],
            [null, null, null, null, null, null, null, "CERTIFICADO N°01 - JUN/26", null, null, null, "S/ CCYR"]);

        var a = ImportadorCertificado.Analizar(hoja);

        Assert.Null(a.Error);
        Assert.Equal(2, a.Secciones.Count);
        Assert.Equal("LINEA B - ESTACION LACROZE", a.Secciones[0].Titulo);
        Assert.Equal("LINEA E - ESTACION VIRREYES", a.Secciones[1].Titulo);
        Assert.Equal(52883392.14m, a.Secciones[0].TotalMontoActualPlanilla);
        Assert.Equal(8141726.84m, a.Secciones[1].TotalMontoActualPlanilla);
        Assert.Equal("ELV.1.1", Assert.Single(a.Secciones[1].Filas).Codigo);   // el mismo código en cada sección
    }

    [Fact]
    public void SeccionDemasiasSinEncabezadoPropioHeredaColumnas()
    {
        var hoja = Hoja(
            [null, "CERTIFICADO N°14 - JUNIO/26"],
            EncabezadoCertificado,
            SubEncabezadoCertificado,
            [null, "PEV-GE.01.1", "Obrador", "Gl", 1, 13273219.03, 13273219.03, Pct(1), null, Pct(1), 13273219.03, 0, 13273219.03],
            [null, null, null, null, null, "TOTAL LÍNEA D - ESTACIÓN AGÜERO=", 4025136349.28, Pct(0.9692), Pct(0), Pct(0.9692), 3901235548.81, 0, 3901235548.81],
            [null, null, null, null, null, "S/LIHUE", 4025136349.28],
            Blanco,
            [null, "DEMASIAS"],
            [null, "GENERALES DE PROYECTO"],
            [null, "PEV-GE.01.1", "Obrador", "Gl", 0.88, 13273219.03, 11680432.75, Pct(0), Pct(0.25), Pct(0.25), 0, 2920108.19, 2920108.19],
            [null, "ÍTEMS NUEVOS"],
            [null, null, "Limpieza, Retiro y Demoliciones"],
            [null, "N-PEV-SD-EAG-01.24.1", "Demolición de Revoques", "m2", 260, 71884.91, 18690076.6, Pct(0), Pct(0.5), Pct(0.5), 0, 9345038.3, 9345038.3],
            [null, "TOTAL DEMASIAS", null, null, null, null, 1344825550.45, Pct(0), Pct(0.01), Pct(0.01), 0, 12265146.49, 12265146.49],
            [null, null, null, null, null, "S/LIHUE"],
            [null, null, null, null, "TOTAL ECONOMIA", null, 176586404.62],
            [null, null, null, null, null, null, null, "CERTIFICADO N°14 - JUNIO/26"]);

        var a = ImportadorCertificado.Analizar(hoja);

        Assert.Equal(2, a.Secciones.Count);
        var demasias = a.Secciones[1];
        Assert.Equal("DEMASIAS", demasias.Titulo);
        Assert.Equal(["PEV-GE.01.1", "N-PEV-SD-EAG-01.24.1"], demasias.Filas.Select(f => f.Codigo));
        Assert.Equal(25m, demasias.Filas[0].PctActual);
        Assert.Equal(12265146.49m, demasias.TotalMontoActualPlanilla);
    }

    [Fact]
    public void EncabezadoDeUnaFilaYTotalConPalabraAlFinal()
    {
        // Premetro: Anterior/Actual en la misma fila del encabezado y el cierre dice
        // «… - TOTAL C/ IVA» en la columna del código.
        var hoja = Hoja(
            [null, "CERTIFICADO DE OBRA N°20 - AGOSTO 2026"],
            Blanco,
            [null, "LOOP PREMETRO EN BARRIO GRAL. SAVIO - PUESTA EN VALOR PARADORES", null, null, null, "PRECIOS BÁSICOS - JULIO 2024", null, "ACTA DE MEDICIÓN", null, null, "CERTIFICADO"],
            [null, "Item", "Descripción", "U", "Cant.", "Precio Unitario", "Precio  Subtotal", "ANTERIOR", "ACTUAL", "ACUMULADO", "ANTERIOR", "ACTUAL", "ACUMULADO"],
            [null, "SP24-LOS-GE-01", "GENERALES DE PROYECTO", null, null, null, null, null, null, null, null, null, null, 5581453.34],
            [null, "SP24-LOS-GE-01.3", "Vigilancia", "Gl", 1, 95919103.24, 95919103.24, Pct(0.92), Pct(0.02), Pct(0.94), 88245574.98, 1918382.06, 90163957.05],
            [null, "LOOP PREMETRO EN BARRIO GRAL. SAVIO - PUESTA EN VALOR PARADORES - TOTAL C/ IVA", null, null, null, null, 12155120188.51, Pct(0.4865), Pct(0.0346), Pct(0.5211), 5913852520.26, 420023570.66, 6333876090.91],
            [null, null, null, null, null, "S/ LOOP PREMETRO XAPOR S.A. y LX ARGENTINA S.A. UT", 12155120188.51]);

        var a = ImportadorCertificado.Analizar(hoja);

        Assert.Null(a.Error);
        Assert.Equal((20, 8, 2026), (a.Numero, a.Mes, a.Anio));
        var s = Assert.Single(a.Secciones);
        Assert.Equal("LOOP PREMETRO EN BARRIO GRAL. SAVIO - PUESTA EN VALOR PARADORES", s.Titulo);
        var vigilancia = Assert.Single(s.Filas);
        Assert.Equal(2m, vigilancia.PctActual);
        Assert.Equal(92m, vigilancia.PctAnterior);
        Assert.Equal(420023570.66m, s.TotalMontoActualPlanilla);
    }

    [Fact]
    public void Cabecera_ExigeElNumero_UnMesSueltoNoLaReemplaza()
    {
        // La cabecera puede estar en la columna A aunque «Item» esté en B.
        var hoja = Hoja(
            [null, "Fecha de inicio: Marzo 2024"],
            ["CERTIFICADO N°14 - JUNIO/26"],
            EncabezadoCertificadoUnaFila,
            [null, "A.1", "Ítem", "u", 1, 2, 2, null, Pct(0.5), Pct(0.5), 0, 1, 1],
            [null, "TOTAL", null, null, null, null, 2, null, Pct(0.5), Pct(0.5), 0, 1, 1]);

        var a = ImportadorCertificado.Analizar(hoja);

        Assert.Equal("CERTIFICADO N°14 - JUNIO/26", a.Encabezado);
        Assert.Equal((14, 6, 2026), (a.Numero, a.Mes, a.Anio));
    }

    [Fact]
    public void Combinar_UneSeccionesYElRenglonRepetidoSeAvisa()
    {
        var s1 = Seccion(new FilaCertificadoImport { Codigo = "A.1", PctActual = 5 });
        s1.TotalMontoActualPlanilla = 50m;
        var s2 = Seccion(new FilaCertificadoImport { Codigo = "A.3", PctActual = 10 }, new FilaCertificadoImport { Codigo = "A.1", PctActual = 99 });
        s2.TotalMontoActualPlanilla = 1m;

        var union = ImportadorCertificado.Combinar([s1, s2]);
        var r = ImportadorCertificado.Aplicar(union, Bloque());

        Assert.Equal(3, union.Filas.Count);
        Assert.Equal(51m, union.TotalMontoActualPlanilla);
        Assert.Equal(5m, r.Porcentajes[2]);                 // se conserva el primer renglón
        Assert.Equal(10m, r.Porcentajes[5]);
        Assert.Equal(1, r.Repetidos);
        Assert.Same(s1, ImportadorCertificado.Combinar([s1]));
    }

    [Fact]
    public void Combinar_SiUnaSeccionNoCierraConTotal_LaUnionNoTieneTotal()
    {
        // Una suma parcial daría una diferencia falsa contra el monto calculado.
        var conTotal = Seccion(new FilaCertificadoImport { Codigo = "A.1", PctActual = 5 });
        var sinTotal = SeccionCon(1, "S2", "A.3");

        Assert.Null(ImportadorCertificado.Combinar([conTotal, sinTotal]).TotalMontoActualPlanilla);
    }

    [Fact]
    public void SinColumnaActual_NoEsHojaDeCertificado()
    {
        var desglose = Hoja(EncabezadoDesglose, ["A.1", "Ítem", "u", 1, 2, 2]);
        Assert.False(ImportadorCertificado.EsHojaDeCertificado(desglose));
        Assert.NotNull(ImportadorCertificado.Analizar(desglose).Error);
    }

    // ── Aplicar sobre un bloque ──────────────────────────────────────────────────

    private static BloqueCertificadoVM Bloque() => new()
    {
        CertificadoEstructuraId = 7,
        Titulo = "Básico",
        Items =
        [
            new() { ItemEstructuraId = 1, EsAgrupador = true, Descripcion = "Rubro" },
            new() { ItemEstructuraId = 2, Codigo = "A.1", CantidadContrato = 100, PUBasico = 10, MontoContrato = 1000, PorcentajeAnterior = 10 },
            new() { ItemEstructuraId = 3, Codigo = "A.2", MontoContrato = 5000 },
            new() { ItemEstructuraId = 4, Codigo = "A.2", MontoContrato = 1 },   // repetido en el bloque
            new() { ItemEstructuraId = 5, Codigo = "A.3", MontoContrato = 1 }
        ]
    };

    private static SeccionCertificadoImport Seccion(params FilaCertificadoImport[] filas)
    {
        var s = new SeccionCertificadoImport { Indice = 0, Titulo = "S", TotalMontoActualPlanilla = 123m };
        s.Filas.AddRange(filas);
        return s;
    }

    [Fact]
    public void Aplicar_MatcheaPorCodigoNormalizadoYCalculaElMonto()
    {
        var r = ImportadorCertificado.Aplicar(Seccion(
            new() { Codigo = " a.1 ", PctActual = 5, PctAnterior = 10 },
            new() { Codigo = "A.2", PctActual = 50 },
            new() { Codigo = "X.9", PctActual = 1 }), Bloque());

        Assert.Equal(7, r.CertificadoEstructuraId);
        Assert.Equal(new Dictionary<int, decimal> { [2] = 5m }, r.Porcentajes);
        Assert.Equal(1, r.Coincidentes);
        Assert.Equal(1, r.Repetidos);
        Assert.Equal(1, r.Desconocidos);
        Assert.Equal(0, r.AnterioresDistintos);
        Assert.Equal(50m, r.TotalMontoMesCalculado);          // 5 % de 100 un × $10
        Assert.Equal(123m, r.TotalMontoMesPlanilla);
        Assert.Equal([3, 4, 5], r.ItemsSinFila.Select(i => i.ItemEstructuraId));
        Assert.Equal(2, r.Problemas.Count());
    }

    [Fact]
    public void Aplicar_ActualIlegible_NoSeAplicaNiSeAsumeCero()
    {
        var hoja = Hoja(
            EncabezadoCertificadoUnaFila,
            [null, "A.1", "Ítem", "u", 100, 10, 1000, null, "s/d", null, 0, 0, 0],
            [null, "A.3", "Ítem", "u", 1, 1, 1, null, "12,5%", null, 0, 0, 0]);
        var seccion = Assert.Single(ImportadorCertificado.Analizar(hoja).Secciones);

        var r = ImportadorCertificado.Aplicar(seccion, Bloque());

        Assert.True(seccion.Filas[0].ActualIlegible);
        Assert.False(r.Porcentajes.ContainsKey(2));
        Assert.Equal(1, r.Ilegibles);
        Assert.Equal(12.5m, r.Porcentajes[5]);     // texto con % → puntos, no fracción
    }

    [Fact]
    public void PorcentajeAbsurdoOErrorDeExcel_EsIlegible()
    {
        // Un 1E27 con formato % desbordaba el decimal al pasarlo a puntos, y el error cortaba
        // la sesión desde el diálogo; un #REF! no es 0 %.
        var hoja = Hoja(
            EncabezadoCertificadoUnaFila,
            [null, "A.1", "Ítem", "u", 1, 10, 10, null, Pct(1E27), null, 0, 0, 0],
            [null, "A.2", "Ítem", "u", 1, 10, 10, null, 5000, null, 0, 0, 0],
            [null, "A.3", "Ítem", "u", 1, 10, 10, null, ErrorDeExcel("#REF!"), null, 0, 0, 0],
            [null, "A.4", "Ítem", "u", 1, 10, 10, null, Pct(-0.1), null, 0, 0, 0]);

        var filas = Assert.Single(ImportadorCertificado.Analizar(hoja).Secciones).Filas;

        Assert.Equal([true, true, true, false], filas.Select(f => f.ActualIlegible));
        Assert.All(filas.Take(3), f => Assert.Null(f.PctActual));
        Assert.Equal(-10m, filas[3].PctActual);
    }

    [Fact]
    public void Aplicar_AnteriorDistintoSeAplicaPeroSeAvisa_YVacioEsCero()
    {
        var r = ImportadorCertificado.Aplicar(Seccion(
            new() { Codigo = "A.1", PctActual = 5, PctAnterior = 12 },
            new() { Codigo = "A.3", PctActual = null, PctAnterior = null }), Bloque());

        Assert.Equal(5m, r.Porcentajes[2]);
        Assert.Equal(0m, r.Porcentajes[5]);
        Assert.Equal(1, r.AnterioresDistintos);
        Assert.Equal(2, r.Coincidentes);
        var aviso = Assert.Single(r.Problemas);
        Assert.Equal(EstadoFilaCertificado.AnteriorDistinto, aviso.Estado);
        Assert.Equal(10m, aviso.PctAnteriorSistema);
    }

    // ── Elección de hoja ─────────────────────────────────────────────────────────

    /// <summary>Hoja de certificado: cabecera, encabezado, un renglón por código (% del mes, null = vacío) y TOTAL.</summary>
    private static HojaExcel HojaCert(string nombre, string cabecera, params (string Codigo, double? Pct)[] renglones) =>
        Hoja(nombre,
        [
            [null, cabecera],
            EncabezadoCertificadoUnaFila,
            .. renglones.Select(r => new object?[] { null, r.Codigo, "Ítem", "u", 1, 10, 10, null, r.Pct is double p ? Pct(p) : null, null, 0, 0, 0 }),
            [null, "TOTAL", null, null, null, null, 10]
        ]);

    [Fact]
    public void EvaluarHojas_SaltaElResumenSinRenglones_YLaHojaEnDolaresNoEsAlternativa()
    {
        // Premetro: «RESUMEN $» tiene el encabezado pero no renglones; la hoja en dólares
        // repite la cabecera con pocos renglones (no es otra versión del certificado).
        var resumen = Hoja("RESUMEN $", EncabezadoCertificadoUnaFila, [null, "TOTAL", null, null, null, null, 10]);
        var pesos = HojaCert("CERTIFICADO EN PESOS", "CERTIFICADO DE OBRA N°20 - AGOSTO 2026", ("A.1", 0.1), ("A.2", null), ("A.3", 0.5));
        var dolares = HojaCert("CERTIFICADO EN DOLARES", "CERTIFICADO DE OBRA N°20 - AGOSTO 2026", ("U.1", 0.1));

        var e = ImportadorCertificado.EvaluarHojas(new LibroExcel([resumen, pesos, dolares]), 20, 8, 2026);

        Assert.Same(pesos, e.Elegida?.Hoja);
        Assert.Empty(e.Alternativas);
        Assert.Equal(["RESUMEN $ — sin renglones", "CERTIFICADO EN PESOS — N°20 ago/2026", "CERTIFICADO EN DOLARES — N°20 ago/2026"],
            e.Opciones.Select(o => o.Etiqueta));
    }

    [Fact]
    public void EvaluarHojas_PrefiereLaQueCoincideConElCertificado_YLaVisibleAnteLaOculta()
    {
        // Agüero N°15: «CERTIFICADO» y «CE A» están ocultas y son copias del N°14; la vigente
        // es «CE B», aunque la copia vieja tenga más % cargados.
        var vieja = Oculta(HojaCert("CERTIFICADO", "CERTIFICADO N°14 - JUNIO/26", ("A.1", 0.1), ("A.2", 0.2)));
        var ceA = Oculta(HojaCert("CE A", "CERTIFICADO N°14 - JUNIO/26", ("A.1", null)));
        var ceB = HojaCert("CE B", "CERTIFICADO N°15 - JULIO/26", ("A.1", 0.25), ("A.2", null));

        var e = ImportadorCertificado.EvaluarHojas(new LibroExcel([vieja, ceA, ceB]), 15, 7, 2026);

        Assert.Same(ceB, e.Elegida?.Hoja);
        Assert.Empty(e.Alternativas);
        Assert.Equal("CERTIFICADO — N°14 jun/2026 · oculta", e.Opciones[0].Etiqueta);

        // A igual N° y período, la visible gana aunque la oculta tenga más renglones.
        var copiaOculta = Oculta(HojaCert("COPIA", "CERTIFICADO N°15 - JULIO/26", ("A.1", 0.1), ("A.2", 0.1), ("A.3", 0.1)));
        Assert.Same(ceB, ImportadorCertificado.EvaluarHojas(new LibroExcel([copiaOculta, ceB]), 15, 7, 2026).Elegida?.Hoja);
    }

    [Fact]
    public void EvaluarHojas_HojasParalelasDelMismoCertificado_EligeLaDeMasPorcentajesYLasAvisa()
    {
        // Agüero N°14: tres hojas visibles dicen «CERTIFICADO N°14 - JUNIO/26»; «CERTIFICADO»
        // y «CE A» tienen todos los % en cero y los valores reales están en «CE B».
        const string cabecera = "CERTIFICADO N°14 - JUNIO/26";
        var enCero = HojaCert("CERTIFICADO", cabecera, ("A.1", null), ("A.2", null), ("A.3", null));
        var ceA = HojaCert("CE A", cabecera, ("A.1", null), ("A.2", null));
        var ceB = HojaCert("CE B", cabecera, ("A.1", 0.1), ("A.2", null), ("A.3", 0.3));

        var e = ImportadorCertificado.EvaluarHojas(new LibroExcel([enCero, ceA, ceB]), 14, 6, 2026);

        Assert.Same(ceB, e.Elegida?.Hoja);
        Assert.Equal([enCero, ceA], e.Alternativas.Select(o => o.Hoja));
    }

    [Fact]
    public void EvaluarHojas_UnaHojaQueFalla_QuedaMarcadaYNoImpideUsarLasDemas()
    {
        // Una fila nula simula un error inesperado del parser en una hoja ajena.
        var rota = new HojaExcel("Rota", [null!]);
        var cert = HojaCert("CE B", "CERTIFICADO N°15 - JULIO/26", ("A.1", 0.25));

        var e = ImportadorCertificado.EvaluarHojas(new LibroExcel([rota, cert]), 15, 7, 2026);

        Assert.Same(cert, e.Elegida?.Hoja);
        var fallida = e.Opciones[0];
        Assert.Equal("Rota — no se pudo analizar", fallida.Etiqueta);
        Assert.NotNull(fallida.Falla);
        Assert.False(fallida.Utilizable);
        Assert.Contains("no se pudo analizar", fallida.Analisis?.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EncabezadoSinActualSeguidoDeFilaCorta_NoBloqueaElLibro()
    {
        // Regresión: una hoja ajena con «Item | Descripción» en la columna C y una nota en A
        // debajo hacía arrancar Array.FindIndex fuera de rango y el error tapaba todo el libro.
        var resumen = Hoja("Resumen", [null, null, "Item", "Descripción", "U"], ["Nota al pie"]);
        var cert = HojaCert("CERTIFICADO", "CERTIFICADO N°1 - MAYO/25", ("A.1", 0.1));

        var e = ImportadorCertificado.EvaluarHojas(new LibroExcel([resumen, cert]), 1, 5, 2025);

        Assert.False(ImportadorCertificado.EsHojaDeCertificado(resumen));
        Assert.Same(cert, e.Elegida?.Hoja);
    }

    // ── Mapeo inicial sección → bloque ───────────────────────────────────────────

    private static BloqueCertificadoVM BloqueCon(int id, string titulo, params string[] codigos) => new()
    {
        CertificadoEstructuraId = id,
        Titulo = titulo,
        Orden = id,
        Items =
        [
            new() { ItemEstructuraId = id * 100, EsAgrupador = true, Descripcion = "Rubro" },
            .. codigos.Select((c, i) => new ItemCertificadoVM { ItemEstructuraId = id * 100 + i + 1, Codigo = c })
        ]
    };

    private static SeccionCertificadoImport SeccionCon(int indice, string titulo, params string[] codigos)
    {
        var s = new SeccionCertificadoImport { Indice = indice, Titulo = titulo };
        s.Filas.AddRange(codigos.Select(c => new FilaCertificadoImport { Codigo = c }));
        return s;
    }

    [Fact]
    public void MapeoInicial_CadaSeccionAlBloqueQueTieneSusCodigos()
    {
        // Agüero N°14: las demasías repiten códigos del básico (A.1, A.2) y suman ítems nuevos (N-…).
        var basico = BloqueCon(1, "Básico", "A.1", "A.2", "A.3", "A.4");
        var bed = BloqueCon(2, "BED N°1", "A.1", "A.2", "N-A.9");

        var mapeo = ImportadorCertificado.MapeoInicial(
        [
            SeccionCon(0, "Sección 1", "A.1", "A.2", "A.3", "A.4"),   // 2 de 4 en el BED: no alcanza
            SeccionCon(1, "DEMASIAS", "A.1", "A.2", "N-A.9"),
            SeccionCon(2, "Sección 3", "A.2", "N-A.9")                // demasías sin título reconocible
        ], [basico, bed]);

        Assert.Equal(1, mapeo[0]);
        Assert.Equal(2, mapeo[1]);
        Assert.Equal(2, mapeo[2]);   // 2 de 2 en el BED contra 1 de 2 en el básico
    }

    [Fact]
    public void MapeoInicial_DemasiasNoSeSugierenEnUnBloqueQueNoLoDice()
    {
        // Sin bloque BED en el certificado, una sección de demasías cuyos códigos están todos
        // en el básico no se sugiere: aplicarla pisaría los % del básico en silencio.
        var basico = BloqueCon(1, "Básico", "A.1", "A.2", "A.3");

        var mapeo = ImportadorCertificado.MapeoInicial([SeccionCon(0, "DEMASIAS", "A.1", "A.2")], [basico]);

        Assert.Null(mapeo[0]);
    }

    [Fact]
    public void MapeoInicial_VariasSeccionesPuedenIrAlMismoBloque()
    {
        // Lacroze: una sección por estación y un solo bloque básico con las dos.
        var basico = BloqueCon(1, "Básico", "ELV.2.1", "ELV.2.2", "ELV.3.1", "ELV.3.2");

        var mapeo = ImportadorCertificado.MapeoInicial(
        [
            SeccionCon(0, "LINEA B - ESTACION LACROZE", "ELV.2.1", "ELV.2.2"),
            SeccionCon(1, "LINEA E - ESTACION VIRREYES", "ELV.3.1", "ELV.3.2")
        ], [basico]);

        Assert.Equal(1, mapeo[0]);
        Assert.Equal(1, mapeo[1]);
    }

    [Fact]
    public void SeccionesDeContinuacionDeDemasias_HeredanElTituloYLaGuarda()
    {
        // DEMASIAS con subtotales por rubro: los tramos que siguen a cada subtotal no tienen
        // encabezado propio. Sin heredar el título quedaban «Sección N» y, sin bloque BED,
        // se sugerían en el básico (sus códigos están ahí) y pisaban sus %.
        var hoja = Hoja(
            [null, "CERTIFICADO N°14 - JUNIO/26"],
            EncabezadoCertificadoUnaFila,
            [null, "A.1", "Ítem", "u", 1, 10, 10, null, Pct(0.1), null, 0, 1, 1],
            [null, "TOTAL BÁSICO", null, null, null, null, 10],
            [null, "DEMASIAS"],
            [null, "A.1", "Ítem", "u", 1, 10, 10, null, Pct(0.2), null, 0, 2, 2],
            [null, "SUBTOTAL GENERALES", null, null, null, null, 10],
            [null, "A.2", "Ítem", "u", 1, 10, 10, null, Pct(0.5), null, 0, 5, 5],
            [null, "SUBTOTAL ESTACIÓN", null, null, null, null, 10],
            [null, "GENERALES DE PROYECTO"],
            [null, "A.3", "Ítem", "u", 1, 10, 10, null, Pct(0.5), null, 0, 5, 5],
            [null, "TOTAL DEMASIAS", null, null, null, null, 20],
            [null, "ECONOMIAS"],
            [null, "A.2", "Ítem", "u", 1, 10, 10, null, Pct(-0.1), null, 0, -1, -1],
            [null, "TOTAL ECONOMIAS", null, null, null, null, -1]);

        var a = ImportadorCertificado.Analizar(hoja);

        Assert.Equal(["Sección 1", "DEMASIAS", "DEMASIAS (cont.)", "DEMASIAS › GENERALES DE PROYECTO", "ECONOMIAS"],
            a.Secciones.Select(s => s.Titulo));
        var mapeo = ImportadorCertificado.MapeoInicial(a.Secciones, [BloqueCon(1, "Básico", "A.1", "A.2", "A.3")]);
        Assert.Equal(1, mapeo[0]);
        Assert.Null(mapeo[1]);
        Assert.Null(mapeo[2]);
        Assert.Null(mapeo[3]);
        Assert.Equal(1, mapeo[4]);      // las economías se certifican sobre el básico
    }

    [Fact]
    public void DespuesDelTotalQueNombraElTramo_LaContinuacionYaNoLoHereda()
    {
        // «TOTAL DEMASIAS» cierra el tramo. El título siguiente está en la columna de la
        // descripción (no se lee como título): la sección queda «Sección 3», y no «DEMASIAS
        // (cont.)», que se sugeriría en el BED con el −10 % de una economía.
        var hoja = Hoja(
            [null, "CERTIFICADO N°14 - JUNIO/26"],
            EncabezadoCertificadoUnaFila,
            [null, "A.1", "Ítem", "u", 1, 10, 10, null, Pct(0.1), null, 0, 1, 1],
            [null, "TOTAL BÁSICO", null, null, null, null, 10],
            [null, "DEMASIAS"],
            [null, "A.1", "Ítem", "u", 1, 10, 10, null, Pct(0.2), null, 0, 2, 2],
            [null, "TOTAL DEMASIAS", null, null, null, null, 10],
            [null, null, "ECONOMIAS"],
            [null, "A.2", "Ítem", "u", 1, 10, 10, null, Pct(-0.1), null, 0, -1, -1],
            [null, "TOTAL ECONOMIAS", null, null, null, null, -1]);

        var a = ImportadorCertificado.Analizar(hoja);

        Assert.Equal(["Sección 1", "DEMASIAS", "Sección 3"], a.Secciones.Select(s => s.Titulo));
        var mapeo = ImportadorCertificado.MapeoInicial(a.Secciones,
            [BloqueCon(1, "Básico", "A.1", "A.2"), BloqueCon(2, "BED N°1", "A.1", "A.2", "N-A.9")]);
        Assert.Null(mapeo[2]);          // empata entre los dos bloques: la elige el usuario
    }

    [Fact]
    public void EntreSecciones_UnTituloDeEconomiasODemasiasLeGanaAUnoNeutro()
    {
        var hoja = Hoja(
            [null, "CERTIFICADO N°14 - JUNIO/26"],
            EncabezadoCertificadoUnaFila,
            [null, "A.1", "Ítem", "u", 1, 10, 10, null, Pct(0.1), null, 0, 1, 1],
            [null, "TOTAL BÁSICO", null, null, null, null, 10],
            [null, "OBSERVACIONES DE LA INSPECCIÓN"],
            [null, "ECONOMIAS"],
            [null, "A.2", "Ítem", "u", 1, 10, 10, null, Pct(-0.1), null, 0, -1, -1],
            [null, "TOTAL ECONOMIAS", null, null, null, null, -1]);

        var a = ImportadorCertificado.Analizar(hoja);

        Assert.Equal(["Sección 1", "ECONOMIAS"], a.Secciones.Select(s => s.Titulo));
    }

    [Fact]
    public void PorcentajeTipeadoComoTextoEnCeldaConFormatoPorcentaje_SeTomaEnPuntos()
    {
        // «0.5%» con punto queda como texto aunque la celda tenga formato %: ya está en puntos.
        // Regresión: se multiplicaba por 100 y quedaba 50 %, sin pasar el control del 100 %.
        var hoja = Hoja(
            EncabezadoCertificadoUnaFila,
            [null, "A.1", "Ítem", "u", 1, 10, 10, null, new CeldaExcel("0.5%", "0.00%"), null, 0, 0, 0]);

        var fila = Assert.Single(Assert.Single(ImportadorCertificado.Analizar(hoja).Secciones).Filas);

        Assert.Equal(0.5m, fila.PctActual);
    }

    [Fact]
    public void MapeoInicial_PocosCodigosOEmpate_QuedaParaElUsuario()
    {
        var basico = BloqueCon(1, "Básico", "A.1", "A.2", "A.3");
        var estacion1 = BloqueCon(2, "Estación 1", "G.1", "G.2");
        var estacion2 = BloqueCon(3, "Estación 2", "G.1", "G.2");

        var mapeo = ImportadorCertificado.MapeoInicial(
        [
            SeccionCon(0, "Sección 1", "A.1", "X.1", "X.2"),   // 1 de 3: menos de dos tercios
            SeccionCon(1, "GENERALES", "G.1", "G.2")           // 2 de 2 en dos bloques: empate
        ], [basico, estacion1, estacion2]);

        Assert.Null(mapeo[0]);
        Assert.Null(mapeo[1]);
    }
}
