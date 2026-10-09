using SIGO.Models.Enums;
using SIGO.Services.Importacion;
using static SIGO.Tests.HojaExcelBuilder;

namespace SIGO.Tests;

/// <summary>
/// Reglas del parser de planillas de estructura, fijadas con recortes de las planillas
/// reales de tres contratistas: encabezado por contenido, rubro por prefijo de código o
/// por celda TOTAL, sub-rubro sin código, «No aplica» como ítem sin monto, separadores,
/// secciones por fila TOTAL, cotización con PU derivado y balance de BED por grupo.
/// </summary>
public class ImportadorEstructuraTests
{
    // ── Planilla de desglose (Agüero) ────────────────────────────────────────────

    private static HojaExcel DesgloseAguero() => Hoja(
        [null, null, null, null, null, null, null, null, "LICITACIÓN PÚBLICA Nº 243/24"],
        ["OBRA: PUESTA EN VALOR"],
        ["Empresa=", "LIHUE INGENIERIA S.A."],
        ["PEV-SD-EAG-01", "LÍNEA D - ESTACIÓN AGÜERO"],
        Blanco,
        EncabezadoDesglose,
        Blanco,
        Blanco,
        ["PEV-SD-EAG-01.1", "Limpieza, Retiro y Demoliciones", null, null, null, null, 56586333.63, null, null, 0],
        ["PEV-SD-EAG-01.1.1", "Limpieza, Desmontajes, Retiro señalética", "gl", 1, 2004898.86, 2004898.86, null, null, 0],
        ["PEV-SD-EAG-01.1.3", "Desmontaje de troneras de acceso (No Aplica)", null, null, null, null, null, null, 0],
        ["PEV-SD-EAG-01.1.12", "Demolición de Revoques en galerías", "m2", 169.85, 19924.21, 3384127.07, null, null, 0],
        Blanco,
        ["PEV-SD-EAG-01.18", "Instalación Eléctrica", null, null, null, null, 375393535.36, null, null, 0],
        [null, "Desmontaje de bandejas existentes"],
        ["PEV-SD-EAG-01.18.1", "Desmontaje de bandejas existentes de 50mm", "m", 300, 9001.52, 2700456],
        [null, "Intalación de Artefactos"],
        ["PEV-SD-EAG-01.18.8", "Artefacto tipo 2PL", "u", 18, 196357.75, 3534439.5],
        ["-"],
        [null, null, null, null, null, null, null, null, null, 0],
        [null, null, null, null, null, "TOTAL LÍNEA D - ESTACIÓN AGÜERO=", 3807513007.47, null, null, 0],
        Blanco,
        ["Notas:"],
        [null, "Las cantidades indicadas son estimadas p/cotización"]);

    [Fact]
    public void Desglose_DetectaLayoutBloqueYColumnas()
    {
        var a = ImportadorEstructura.Analizar(DesgloseAguero());

        Assert.Null(a.Error);
        Assert.Equal(LayoutEstructura.Desglose, a.Layout);
        Assert.Equal("PEV-SD-EAG-01", a.CodigoBloque);
        Assert.Equal("LÍNEA D - ESTACIÓN AGÜERO", a.NombreBloque);
        Assert.Equal("Código=A, Descripción=B, U=C, Cantidad=D, PU=E, Subtotal=F, Total=G", a.ColumnasDetectadas);
    }

    [Fact]
    public void Desglose_ClasificaRubrosSubRubrosEItems()
    {
        var a = ImportadorEstructura.Analizar(DesgloseAguero());
        var s = Assert.Single(a.Secciones);   // la sección «Notas» no tiene ítems y se descarta
        var f = s.Filas;

        Assert.Equal(
            [TipoFilaEstructura.Rubro, TipoFilaEstructura.Item, TipoFilaEstructura.ItemSinMonto, TipoFilaEstructura.Item,
             TipoFilaEstructura.Rubro, TipoFilaEstructura.SubRubro, TipoFilaEstructura.Item, TipoFilaEstructura.SubRubro, TipoFilaEstructura.Item],
            f.Select(x => x.Tipo));

        // Jerarquía: ítems bajo su rubro; sub-rubros hermanos bajo el rubro; ítems bajo el sub-rubro.
        Assert.Null(f[0].PadreIndice);
        Assert.Equal(0, f[1].PadreIndice);
        Assert.Equal(0, f[2].PadreIndice);
        Assert.Equal(4, f[5].PadreIndice);
        Assert.Equal(5, f[6].PadreIndice);
        Assert.Equal(4, f[7].PadreIndice);   // el segundo sub-rubro cierra al primero
        Assert.Equal(7, f[8].PadreIndice);
        Assert.Equal([0, 1, 1, 1, 0, 1, 2, 1, 2], f.Select(x => x.Nivel));

        // La fila «-» y la que solo tiene un 0 residual no se listan; la fila 9 original es la primera.
        Assert.Equal(9, f[0].Fila);
        Assert.DoesNotContain(f, x => x.Descripcion == "-");
    }

    [Fact]
    public void Desglose_MontoConReglaDeLaAppYTotalDePlanilla()
    {
        var s = ImportadorEstructura.Analizar(DesgloseAguero()).Secciones[0];
        var revoques = s.Filas.Single(x => x.Codigo == "PEV-SD-EAG-01.1.12");

        Assert.Equal(169.85m, revoques.Cantidad);
        Assert.Equal(19924.21m, revoques.PUBasico);
        Assert.Equal(3384127.0685m, revoques.Monto);          // 4 decimales, no el ROUND(,2) de la planilla
        Assert.Equal(3807513007.47m, s.TotalPlanilla);
        Assert.Equal(2004898.86m + 3384127.0685m + 2700456m + 3534439.5m, s.TotalCalculado);
        Assert.Equal(5, s.CantidadItems);          // incluye el «No aplica» sin monto
        Assert.Equal(4, s.Agrupadores);
        Assert.Equal("PEV-SD-EAG-01.1 Limpieza, Retiro y Demoliciones", s.Titulo);
        Assert.Equal("PEV-SD-EAG-01.1 Limpieza, Retiro y Demoliciones (filas 9–21)", s.Etiqueta);
    }

    [Fact]
    public void Desglose_NoAplicaConCodigoEsItemSinMontoNoRubro()
    {
        var s = ImportadorEstructura.Analizar(DesgloseAguero()).Secciones[0];
        var noAplica = s.Filas.Single(x => x.Codigo == "PEV-SD-EAG-01.1.3");

        Assert.Equal(TipoFilaEstructura.ItemSinMonto, noAplica.Tipo);
        Assert.True(noAplica.SeImporta);
        Assert.Null(noAplica.Monto);
        Assert.Contains("monto vacío", noAplica.Detalle);
    }

    // ── Variantes de otras contratistas ──────────────────────────────────────────

    [Fact]
    public void Desglose_ColumnaAuxiliarAntesDelCodigoYRubroVacioConTotalCero()
    {
        // Premetro: columna A con código corto, «Item» en B. Lacroze: rubro sin ítems con
        // TOTAL 0 seguido de «No Aplica» sin código, y fila fantasma con solo la unidad.
        var hoja = Hoja(
            [null, "SP24-LOS-PNO-01", "PREMETRO - PARADOR NORTE"],
            [null, "Ítem", "Descripción", "U", "Cantidad ", "Precio Unitario $", "Precio Subtotal $", "TOTAL $"],
            ["01.1", "SP24-LOS-PNO-01.1", "Limpieza, Retiro y Demoliciones", null, null, null, null, 0],
            ["01.1.1", "SP24-LOS-PNO-01.1.1", "Limpieza, Desmonte", "gl", 1, 0, 0],
            ["01.2", "SP24-LOS-PNO-01.2", "Movimiento de Suelos", null, null, null, null, 0],
            [null, null, "No Aplica", "m3", null, 0, 0],
            ["01.3", "SP24-LOS-PNO-01.3", "Cielorrasos", null, null, null, null, 0],
            [null, null, null, "m2", null, null, 0],
            [null, null, null, null, null, null, "TOTAL PREMETRO - PARADOR NORTE=", 0]);

        var a = ImportadorEstructura.Analizar(hoja);

        Assert.Null(a.Error);
        Assert.Equal("SP24-LOS-PNO-01", a.CodigoBloque);
        var f = Assert.Single(a.Secciones).Filas;
        Assert.Equal(
            [TipoFilaEstructura.Rubro, TipoFilaEstructura.ItemSinMonto, TipoFilaEstructura.Rubro, TipoFilaEstructura.ItemSinCodigo, TipoFilaEstructura.Rubro],
            f.Select(x => x.Tipo));
        Assert.Equal(2, f[3].PadreIndice);
        Assert.Equal(0m, f[3].PUBasico);
    }

    [Fact]
    public void Desglose_DescripcionLargaYCodigoRepetidoSeMarcan()
    {
        var larga = new string('x', 520);
        var hoja = Hoja(
            EncabezadoDesglose,
            ["R.1", "Rubro", null, null, null, null, 10],
            ["R.1.1", larga, "u", 2, 5, 10],
            ["R.1.1", "Código repetido", "u", 1, 1, 1]);

        var f = ImportadorEstructura.Analizar(hoja).Secciones[0].Filas;

        Assert.Equal(500, f[1].Descripcion.Length);
        Assert.Contains("recortada", f[1].Detalle);
        Assert.Contains("repetido", f[1].Detalle);
        Assert.Contains("repetido", f[2].Detalle);
        Assert.True(f[1].SeImporta);
    }

    [Fact]
    public void CodigoYUnidadMasLargosQueLaColumna_SeRecortanConAviso()
    {
        // Sin recortar, la importación entera fallaba al guardar por una sola celda desbordada.
        var codigo = "A.1." + new string('9', 260);
        var hoja = Hoja(EncabezadoDesglose, [codigo, "Ítem", new string('u', 300), 1, 2, 2]);

        var f = ImportadorEstructura.Analizar(hoja).Secciones[0].Filas[0];

        Assert.Equal(TipoFilaEstructura.Item, f.Tipo);
        Assert.Equal(codigo[..250], f.Codigo);
        Assert.Equal(250, f.Unidad!.Length);
        Assert.Contains("Código recortado a 250 caracteres", f.Detalle);
        Assert.Contains("Unidad recortada a 250 caracteres", f.Detalle);
    }

    [Fact]
    public void CantidadConMasDeCuatroDecimales_SeRedondeaYSeAvisa()
    {
        var hoja = Hoja(
            EncabezadoDesglose,
            ["ELV.3.5.2", "Hormigón de recalces", "m3", 6.88905, 63421.2, 436911.82]);

        var f = ImportadorEstructura.Analizar(hoja).Secciones[0].Filas[0];

        Assert.Equal(6.8890m, f.Cantidad);   // Math.Round a 4 con redondeo bancario, como CalcularMonto
        Assert.Equal(63421.2m, f.PUBasico);
        Assert.Contains("Cantidad redondeada", f.Detalle);
        Assert.DoesNotContain("PU redondeado", f.Detalle);
    }

    [Fact]
    public void CantidadOPrecioEnCero_EsItemSinMontoConMontoCero()
    {
        var hoja = Hoja(
            EncabezadoDesglose,
            ["P.1", "Rubro", null, null, null, null, 10],
            ["P.1.1", "Ítem con cantidad cero", "u", 0, 10, 0]);

        var f = ImportadorEstructura.Analizar(hoja).Secciones[0].Filas[1];

        Assert.Equal(TipoFilaEstructura.ItemSinMonto, f.Tipo);
        Assert.Equal(0m, f.Monto);
        Assert.Contains("en cero: se importa con monto 0", f.Detalle);
        Assert.DoesNotContain("monto vacío", f.Detalle);
    }

    [Fact]
    public void ErrorDeExcel_EnLaCantidadEsItemConAviso_YEnElCodigoNoEsCodigo()
    {
        // Sin unidad y con TOTAL numérico, una fila sin números sería un rubro: el #REF! en
        // la cantidad dice que es un ítem roto. Un #DIV/0! en Item tiene dígito y sin esta
        // regla pasaba por código.
        var hoja = Hoja(
            EncabezadoDesglose,
            ["P.1", "Rubro", null, null, null, null, 10],
            ["P.1.1", "Ítem con la cantidad rota", null, ErrorDeExcel("#REF!"), null, null, 5],
            [ErrorDeExcel("#DIV/0!"), "Ítem con el código roto", "u", 1, 10, 10]);

        var f = ImportadorEstructura.Analizar(hoja).Secciones[0].Filas;

        Assert.Equal(TipoFilaEstructura.ItemSinMonto, f[1].Tipo);
        Assert.Null(f[1].Cantidad);
        Assert.Contains("Cantidad con error de Excel (#REF!): se tomó como vacío", f[1].Detalle);
        Assert.Equal(TipoFilaEstructura.ItemSinCodigo, f[2].Tipo);
        Assert.Null(f[2].Codigo);
        Assert.Equal(0, f[2].PadreIndice);
    }

    [Fact]
    public void GuionEnLaCeldaTotal_NoConvierteElItemEnRubro()
    {
        var hoja = Hoja(
            EncabezadoDesglose,
            ["P.1", "Rubro", null, null, null, null, 10],
            ["P.1.3", "Hormigón de limpieza (No aplica)", "m3", null, null, null, "-"],
            ["P.1.4", "Otro ítem", "u", 1, 10, 10]);

        var f = ImportadorEstructura.Analizar(hoja).Secciones[0].Filas;

        Assert.Equal(TipoFilaEstructura.ItemSinMonto, f[1].Tipo);
        Assert.Equal(0, f[2].PadreIndice);     // bajo el rubro, no bajo el «No aplica»
    }

    [Fact]
    public void NoAplicaConUnidadYTotalCero_EsItemSinMontoNoRubro()
    {
        // Con un 0 numérico (no un guion) en TOTAL: la unidad dice que es un ítem.
        var hoja = Hoja(
            EncabezadoDesglose,
            ["P.1", "Rubro", null, null, null, null, 10],
            ["P.1.3", "Hormigón de limpieza (No aplica)", "m3", null, null, null, 0],
            ["P.1.4", "Otro ítem", "u", 1, 10, 10]);

        var f = ImportadorEstructura.Analizar(hoja).Secciones[0].Filas;

        Assert.Equal(TipoFilaEstructura.ItemSinMonto, f[1].Tipo);
        Assert.Equal("m3", f[1].Unidad);
        Assert.Equal(0, f[2].PadreIndice);     // bajo el rubro, no bajo el «No aplica»
    }

    [Fact]
    public void ItemConCodigoFueraDeOrden_CuelgaDelRubroDeSuCodigo()
    {
        // A.1.2 viene después del sub-rubro codificado A.1.1 y A.1.3 después del rubro A.2
        // (ítems agregados al final, como el 01.10.29 de las hojas de certificado de Agüero).
        var hoja = Hoja(
            EncabezadoDesglose,
            ["A.1", "Rubro A.1", null, null, null, null, 30],
            ["A.1.1", "Sub-rubro codificado", null, null, null, null, 10],
            ["A.1.1.1", "Ítem", "u", 1, 10, 10],
            ["A.1.2", "Ítem del rubro A.1", "u", 2, 10, 20],
            ["A.2", "Rubro A.2", null, null, null, null, 5],
            ["A.2.1", "Ítem", "u", 1, 5, 5],
            ["A.1.3", "Ítem agregado al final", "u", 1, 1, 1],
            ["A.2.2", "Ítem", "u", 1, 1, 1]);

        var f = ImportadorEstructura.Analizar(hoja).Secciones[0].Filas;

        Assert.Equal(["A.1", "A.1.1", "A.1.1.1", "A.1.2", "A.2", "A.2.1", "A.1.3", "A.2.2"], f.Select(x => x.Codigo));
        Assert.Equal(1, f[2].PadreIndice);     // A.1.1.1 → A.1.1
        Assert.Equal(0, f[3].PadreIndice);     // A.1.2 → A.1, no A.1.1
        Assert.Equal(1, f[3].Nivel);
        Assert.Equal(0, f[6].PadreIndice);     // A.1.3 → A.1, no A.2
        Assert.Equal(1, f[6].Nivel);
        Assert.Equal(4, f[7].PadreIndice);     // A.2.2 sigue bajo A.2
    }

    [Fact]
    public void RubroConCodigoFueraDeOrden_CuelgaDelRubroDeSuPrefijo()
    {
        // El rubro A.1.5 se agregó al final, después de A.2: cuelga de A.1 (no de la raíz)
        // y sus ítems y los siguientes de A.1 lo siguen.
        var hoja = Hoja(
            EncabezadoDesglose,
            ["A.1", "Rubro A.1", null, null, null, null, 10],
            ["A.1.1", "Ítem", "u", 1, 10, 10],
            ["A.2", "Rubro A.2", null, null, null, null, 5],
            ["A.2.1", "Ítem", "u", 1, 5, 5],
            ["A.1.5", "Rubro agregado al final", null, null, null, null, 1],
            ["A.1.5.1", "Ítem", "u", 1, 1, 1],
            ["A.1.6", "Ítem del rubro A.1", "u", 1, 1, 1]);

        var f = ImportadorEstructura.Analizar(hoja).Secciones[0].Filas;

        Assert.Equal(TipoFilaEstructura.Rubro, f[4].Tipo);
        Assert.Null(f[2].PadreIndice);         // A.2 en la raíz
        Assert.Equal(0, f[4].PadreIndice);     // A.1.5 → A.1
        Assert.Equal(1, f[4].Nivel);
        Assert.Equal(4, f[5].PadreIndice);     // A.1.5.1 → A.1.5
        Assert.Equal(2, f[5].Nivel);
        Assert.Equal(0, f[6].PadreIndice);     // A.1.6 → A.1
    }

    [Fact]
    public void SeparadorYTotal_ReinicianLosRubrosVistos()
    {
        // Un código fuera de orden solo se busca entre los rubros desde el último separador o
        // TOTAL: los de antes son de otra parte de la hoja (y sus índices, de otra sección).
        var hoja = Hoja(
            EncabezadoDesglose,
            ["A.1", "Rubro A.1", null, null, null, null, 10],
            ["A.1.1", "Ítem", "u", 1, 10, 10],
            [null, "DEMASIAS"],
            ["B.1", "Rubro B.1", null, null, null, null, 5],
            ["A.1.2", "Ítem después del separador", "u", 1, 5, 5],
            [null, null, null, null, null, "TOTAL PRIMERA PARTE", 15],
            ["C.1", "Rubro C.1", null, null, null, null, 3],
            ["C.1.1", "Rubro C.1.1", null, null, null, null, 3],
            ["A.1.3", "Ítem en la sección siguiente", "u", 1, 3, 3]);

        var a = ImportadorEstructura.Analizar(hoja);

        Assert.Equal(2, a.Secciones.Count);
        var primera = a.Secciones[0].Filas;
        Assert.Equal("Separador de sección", primera[2].Detalle);
        Assert.Equal(3, primera[4].PadreIndice);               // bajo B.1, no bajo el A.1 de antes del separador
        Assert.Equal(1, a.Secciones[1].Filas[2].PadreIndice);   // bajo C.1.1: el índice 0 de A.1 sería C.1
    }

    [Theory]
    [InlineData(true, "no tiene la columna Cantidad")]
    [InlineData(false, "no tiene el grupo de columnas «BED»")]
    public void Balance_GrupoBedSinColumnas_ErrorEspecificoYLayoutReconocido(bool conRotuloBed, string error)
    {
        // Con el rótulo «BED» más a la derecha que el subencabezado, que se recorta en su
        // última celda con dato. Regresión: Array.FindIndex arrancaba fuera de rango y la
        // excepción bloqueaba todo el libro.
        object?[] encabezado =
            [null, "Item", "Descripción", "U", "Cantidad", "VALOR UNITARIO", "TOTAL ITEM", "TOTAL RUBRO", "ECONOMIA", null, null, "DEMASIA", null, null, null, null, conRotuloBed ? "BED" : null];
        var hoja = Hoja(
            encabezado,
            [null, null, null, null, null, null, null, null, "CANTIDAD", "VALOR UNITARIO", "SUBTOTAL", "CANTIDAD", "VALOR UNITARIO", "SUBTOTAL"],
            [null, "A.1", "Ítem", "u", 1, 2, 2, null, null, null, null, 3, 2, 6]);

        Assert.Equal(LayoutEstructura.Balance, ImportadorEstructura.DetectarLayout(hoja));
        var a = ImportadorEstructura.Analizar(hoja, GrupoBalance.Bed);

        Assert.Equal(LayoutEstructura.Balance, a.Layout);
        Assert.Contains(error, a.Error);
        Assert.Null(ImportadorEstructura.Analizar(hoja, GrupoBalance.Demasia).Error);
    }

    [Fact]
    public void NumeroTipeadoComoTexto_SeAvisa()
    {
        var hoja = Hoja(
            EncabezadoDesglose,
            ["P.1", "Rubro", null, null, null, null, 10],
            ["P.1.1", "Ítem", "u", "1.500", 2, 3000]);

        var item = ImportadorEstructura.Analizar(hoja).Secciones[0].Filas[1];

        Assert.Equal(1.5m, item.Cantidad);     // regla INDEC: sin coma, punto decimal
        Assert.Contains("celda de texto", item.Detalle);
    }

    // ── Elección de hoja ─────────────────────────────────────────────────────────

    [Fact]
    public void EvaluarHojas_VisibleConDesgloseAntesQueCotizacion_YLasOcultasAlFinal()
    {
        // BED de Agüero: « Balance EDyA» está oculta y también tiene encabezado de desglose.
        var desgloseOculto = Oculta(Hoja(" Balance EDyA", EncabezadoDesglose, ["A.1", "Ítem", "u", 1, 2, 2]));
        var cotizacion = Hoja("PL_COTIZ", EncabezadoCotizacion, ["A.1", "Ítem", "u", 1, 2]);
        var desglose = Hoja("03-SD-EAG", EncabezadoDesglose, ["A.1", "Ítem", "u", 1, 2, 2]);
        var notas = Hoja("Notas", ["Hola"]);

        var e = ImportadorEstructura.EvaluarHojas(new LibroExcel([desgloseOculto, cotizacion, desglose, notas]));

        Assert.Same(desglose, e.Elegida?.Hoja);
        Assert.Equal(
            [" Balance EDyA — Planilla de desglose · oculta", "PL_COTIZ — Planilla de cotización", "03-SD-EAG — Planilla de desglose", "Notas"],
            e.Opciones.Select(o => o.Etiqueta));
        Assert.Equal([LayoutEstructura.Desglose, LayoutEstructura.Cotizacion, LayoutEstructura.Desglose, (LayoutEstructura?)null],
            e.Opciones.Select(o => o.Layout));
        Assert.Same(cotizacion, ImportadorEstructura.EvaluarHojas(new LibroExcel([desgloseOculto, cotizacion])).Elegida?.Hoja);
        Assert.Same(desgloseOculto, ImportadorEstructura.EvaluarHojas(new LibroExcel([desgloseOculto])).Elegida?.Hoja);
        Assert.Null(ImportadorEstructura.EvaluarHojas(new LibroExcel([notas])).Elegida);
    }

    [Fact]
    public void EvaluarHojas_ConEncabezadoPeroSinItems_VaDespuesDeLasQueTienen()
    {
        // Premetro: la primera hoja («RESUMEN $») trae la fila de encabezado y ningún ítem;
        // la planilla real es la siguiente. Antes se preseleccionaba el resumen.
        var resumen = Hoja("RESUMEN $", EncabezadoDesglose, [null, null, null, null, null, "TOTAL", 0]);
        var desglose = Hoja("CERTIFICADO EN PESOS", EncabezadoDesglose, ["A.1", "Ítem", "u", 1, 2, 2]);

        var e = ImportadorEstructura.EvaluarHojas(new LibroExcel([resumen, desglose]));

        Assert.Same(desglose, e.Elegida?.Hoja);
        Assert.Equal("RESUMEN $ — Planilla de desglose, sin ítems", e.Opciones[0].Etiqueta);
        Assert.False(e.Opciones[0].ConItems);
        Assert.True(e.Opciones[1].ConItems);
        // Una oculta con ítems gana a una visible sin ítems; sola, la sin ítems sigue siendo
        // elegible (la página muestra su error en vez de dejar el desplegable vacío).
        var desgloseOculto = Oculta(desglose);
        Assert.Same(desgloseOculto, ImportadorEstructura.EvaluarHojas(new LibroExcel([resumen, desgloseOculto])).Elegida?.Hoja);
        Assert.Same(resumen, ImportadorEstructura.EvaluarHojas(new LibroExcel([resumen])).Elegida?.Hoja);
    }

    [Fact]
    public void EvaluarHojas_BalanceSinElGrupoPorDefecto_NoCedeAnteUnaOcultaConItems()
    {
        // Un balance sin DEMASIA (solo ECONOMIA y BED) se usa eligiendo otro grupo en la
        // página: no está vacío, así que conserva la preselección frente a una copia oculta.
        var balance = BalanceAguero(conGrupoDemasia: false);
        var copiaOculta = Oculta(Hoja("Copia", EncabezadoDesglose, ["A.1", "Ítem", "u", 1, 2, 2]));

        var e = ImportadorEstructura.EvaluarHojas(new LibroExcel([copiaOculta, balance]));

        Assert.Same(balance, e.Elegida?.Hoja);
        Assert.Equal("Hoja1 — Balance de BED", e.Opciones[1].Etiqueta);
        Assert.True(e.Opciones[1].ConItems);
    }

    [Fact]
    public void EvaluarHojas_SiElAnalisisFallaConEncabezadoValido_ConservaElLayout()
    {
        // Cantidad × PU desborda el decimal: el encabezado se reconoce pero Analizar lanza.
        // La hoja sigue siendo elegible (la página muestra el error al analizarla) en vez de
        // avisar que ninguna hoja tiene encabezado.
        var rota = Hoja("Rota", EncabezadoDesglose, ["A.1", "Ítem", "u", 1e28, 1e28, 1]);

        var e = ImportadorEstructura.EvaluarHojas(new LibroExcel([rota]));

        Assert.Same(rota, e.Elegida?.Hoja);
        Assert.Equal(LayoutEstructura.Desglose, e.Opciones[0].Layout);
        Assert.Equal("Rota — Planilla de desglose, no se pudo analizar", e.Opciones[0].Etiqueta);
        Assert.NotNull(e.Opciones[0].Falla);
        Assert.False(e.Opciones[0].ConItems);
    }

    [Fact]
    public void EvaluarHojas_UnaHojaQueFalla_QuedaMarcadaYNoImpideUsarLasDemas()
    {
        // Una fila nula simula un error inesperado del parser en una hoja ajena.
        var rota = new HojaExcel("Rota", [null!]);
        var desglose = Hoja("Desglose", EncabezadoDesglose, ["A.1", "Ítem", "u", 1, 2, 2]);

        var e = ImportadorEstructura.EvaluarHojas(new LibroExcel([rota, desglose]));

        Assert.Same(desglose, e.Elegida?.Hoja);
        Assert.Equal("Rota — no se pudo analizar", e.Opciones[0].Etiqueta);
        Assert.Null(e.Opciones[0].Layout);
        Assert.NotNull(e.Opciones[0].Falla);
    }

    [Fact]
    public void SinEncabezado_DevuelveError()
    {
        var a = ImportadorEstructura.Analizar(Hoja(["Hola", "Mundo"], ["A", "B", 1]));
        Assert.Equal($"No se encontró el encabezado de la planilla ({ImportadorEstructura.EncabezadoBuscado}).", a.Error);
        Assert.Empty(a.Secciones);
        Assert.Null(ImportadorEstructura.DetectarLayout(Hoja(["Hola"])));
    }

    // ── Planilla de cotización ───────────────────────────────────────────────────

    [Fact]
    public void Cotizacion_DerivaPUYCortaSeccionesPorTotal()
    {
        var hoja = Hoja(
            ["PLANILLA DE COTIZACIÓN"],
            EncabezadoCotizacion,
            ["PEV-GE.01", "GENERALES DE PROYECTO"],
            ["PEV-GE.01.1", "Obrador", "Gl", 1, 13273219.03],
            ["PEV-GE.01.2", "Vigilancia", "Gl", 2, 100],
            [null, null, null, "TOTAL GENERALES DE PROYECTO = ", 13273319.03, 0],
            ["PEV-SD-EAG-01", "LÍNEA D - ESTACIÓN AGÜERO"],
            ["PEV-SD-EAG-01.1", "Limpieza, Retiro y Demoliciones", "Gl", 1, 56586333.63],
            [null, null, null, "TOTAL LÍNEA D = ", 56586333.63, 0]);

        var a = ImportadorEstructura.Analizar(hoja);

        Assert.Equal(LayoutEstructura.Cotizacion, a.Layout);
        Assert.Contains("PU=Subtotal÷Cantidad", a.ColumnasDetectadas);
        Assert.Equal(2, a.Secciones.Count);

        var generales = a.Secciones[0];
        Assert.Equal("PEV-GE.01 GENERALES DE PROYECTO", generales.Titulo);
        Assert.Equal("PEV-GE.01 GENERALES DE PROYECTO (filas 3–6)", generales.Etiqueta);
        Assert.Equal(TipoFilaEstructura.Rubro, generales.Filas[0].Tipo);   // prefijo del siguiente código
        Assert.Equal(13273219.03m, generales.Filas[1].PUBasico);
        Assert.Equal(50m, generales.Filas[2].PUBasico);                     // 100 / 2
        Assert.Equal(13273319.03m, generales.TotalPlanilla);
        Assert.Equal(13273319.03m, generales.TotalCalculado);

        Assert.Equal("PEV-SD-EAG-01 LÍNEA D - ESTACIÓN AGÜERO (filas 7–9)", a.Secciones[1].Etiqueta);
    }

    [Fact]
    public void Cotizacion_SubtotalSinLaPalabraTotalTambienCierraLaSeccion()
    {
        // Lacroze: cada bloque cierra con su nombre en la columna Cantidad y el monto en
        // Subtotal, sin «TOTAL»; solo la última fila dice «TOTAL =».
        var hoja = Hoja(
            EncabezadoCotizacion,
            ["ELV.1", "INGENIERÍA, PROVISIONES Y ESTUDIOS PREVIOS"],
            ["ELV.1.1", "Obrador", "gl", 1, 11808220.82],
            [null, null, null, "INGENIERÍA, PROVISIONES Y ESTUDIOS PREVIOS", 11808220.82, 0],
            ["ELV.2", "ESTACIÓN FEDERICO LACROZE - ASCENSOR Nº1"],
            ["ELV.2.1", "Limpieza, Retiro y Demoliciones", "gl", 1, 1910630.16, 0],
            [null, null, null, "ESTACIÓN FEDERICO LACROZE - ASCENSOR Nº1", 1910630.16, 0],
            ["TOTAL = ", null, null, null, 13718850.98, 0]);

        var a = ImportadorEstructura.Analizar(hoja);

        Assert.Equal(2, a.Secciones.Count);
        Assert.Equal("ELV.1 INGENIERÍA, PROVISIONES Y ESTUDIOS PREVIOS", a.Secciones[0].Titulo);
        Assert.Equal(11808220.82m, a.Secciones[0].TotalPlanilla);
        Assert.Equal(1, a.Secciones[0].CantidadItems);
        Assert.Equal(1910630.16m, a.Secciones[1].TotalPlanilla);
    }

    [Fact]
    public void FilaFantasmaConUnidadYCero_NoCierraLaSeccion()
    {
        // Premetro: un rubro vacío seguido de una fila con solo «m2» y un 0 en TOTAL no es
        // un subtotal (la unidad no cuenta como texto de cierre).
        var hoja = Hoja(
            EncabezadoDesglose,
            ["R.1", "Rubro vacío", null, null, null, null, 0],
            [null, null, "m2", null, null, null, 0],
            ["R.2", "Rubro con ítem", null, null, null, null, 5],
            ["R.2.1", "Ítem", "u", 1, 5, 5]);

        var a = ImportadorEstructura.Analizar(hoja);

        var s = Assert.Single(a.Secciones);
        Assert.Equal(["R.1", "R.2", "R.2.1"], s.Filas.Select(f => f.Codigo));
    }

    // ── Balance de BED ───────────────────────────────────────────────────────────

    // Como la hoja «Balance» real: la columna A lleva un índice en los ítems nuevos y el
    // código va en B; encabezado de dos filas con los grupos ECONOMIA / DEMASIA / BED.
    private static HojaExcel BalanceAguero(bool conCantidadContrato = true, bool conGrupoDemasia = true) => Hoja(
        [null, "Balance de economías y demasías por ajuste de proyecto"],
        Blanco,
        [null, "Item", "Descripción", "U", conCantidadContrato ? "Cantidad" : null, "VALOR            UNITARIO", "TOTAL ITEM", "TOTAL RUBRO", "ECONOMIA", null, null, conGrupoDemasia ? "DEMASIA" : null, null, null, "BED"],
        [null, null, null, null, null, null, null, null, "CANTIDAD", "VALOR UNITARIO", "SUBTOTAL", "CANTIDAD", "VALOR UNITARIO", "SUBTOTAL", "CANTIDAD", "VALOR UNITARIO", "SUBTOTAL"],
        Blanco,
        [null, "GENERALES DE PROYECTO", null, null, null, null, null, 217623341.81],
        [null, "PEV-GE.01.1", "Obrador", "Gl", 1, 13273219.03, 13273219.03, null, null, 0, 0, 0.88, 13273219.03, 11680432.75, 1.88, 13273219.03, 24953651.78],
        [null, "PEV-GE.01.6", "Protección física", "Gl", 1, 1785215.93, 1785215.93, null, null, 0, 0, null, 0, 0, 1, 1785215.93, 1785215.93],
        [null, "PEV-SD-EAG-01.10", "Carpintería Metálica", null, null, null, null, 77866800.56],
        [null, "PEV-SD-EAG-01.10.8", "PR1", "u", 3, 2506159.83, 7518479.49, null, 3, 2506159.83, 7518479.49, null, 0, 0, 0, 0, 0],
        [null, "PEV-SD-EAG-01.10.17", "Puertas de Esc Mec", "u", 8, 1255160.14, 10041281.12, null, 1.2, 1255160.14, 1506192.17, null, 0, 0, 6.8, 1255160.14, 8535088.95],
        Blanco,
        [null, null, "ÍTEMS NUEVOS"],
        [null, null, "Limpieza, Retiro y Demoliciones", null, null, null, null, 0],
        [1, "N-PEV-SD-EAG-01.24.1", "Demolición de Revoques en sector sobre vías", "m2", null, 71884.91, 0, null, null, null, 0, 260, 71884.91, 18690076.6, 260, 71884.91, 18690076.6],
        [null, null, "Instalación Eléctrica", null, null, null, null, 0],
        [null, null, "Intalación de Artefactos"],
        [5, "N-PEV-SD-EAG-01.24.5", "Artefacto tipo 1PLE 1000mm", "u", null, 436222.34, 0, null, null, null, 0, 5, 436222.34, 2181111.7, 5, 436222.34, 2181111.7],
        Blanco,
        [null, "TOTAL LÍNEA D - ESTACIÓN AGÜERO=", null, null, null, null, null, 4025136349.28, null, null, 176586402.05, null, null, 1344825550.45, null, null, 5193375497.68]);

    [Fact]
    public void Balance_GrupoDemasia_ImportaDemasiasYNuevosYOmiteEconomias()
    {
        var hoja = BalanceAguero();
        Assert.Equal(LayoutEstructura.Balance, ImportadorEstructura.DetectarLayout(hoja));

        var a = ImportadorEstructura.Analizar(hoja, GrupoBalance.Demasia);

        Assert.Null(a.Error);
        var s = Assert.Single(a.Secciones);
        var f = s.Filas;

        var obrador = f.Single(x => x.Codigo == "PEV-GE.01.1");
        Assert.Equal(TipoFilaEstructura.Item, obrador.Tipo);
        Assert.Equal(0.88m, obrador.Cantidad);
        Assert.Equal(13273219.03m, obrador.PUBasico);
        Assert.Equal(TipoMovimiento.Demasia, obrador.TipoMovimiento);

        Assert.Equal(TipoFilaEstructura.Ignorada, f.Single(x => x.Codigo == "PEV-GE.01.6").Tipo);      // sin demasía
        Assert.Equal(TipoFilaEstructura.Economia, f.Single(x => x.Codigo == "PEV-SD-EAG-01.10.8").Tipo); // solo economía
        Assert.Equal(TipoFilaEstructura.Economia, f.Single(x => x.Codigo == "PEV-SD-EAG-01.10.17").Tipo);
        Assert.Equal(TipoFilaEstructura.Ignorada, f.Single(x => x.Codigo == "PEV-SD-EAG-01.10").Tipo);   // rubro sin ítems en el grupo
        Assert.Equal("Separador de sección", f.Single(x => x.Descripcion == "ÍTEMS NUEVOS").Detalle);

        var nuevo = f.Single(x => x.Codigo == "N-PEV-SD-EAG-01.24.1");
        Assert.Equal(TipoFilaEstructura.Item, nuevo.Tipo);
        Assert.Equal(260m, nuevo.Cantidad);
        Assert.Equal(71884.91m, nuevo.PUBasico);
        Assert.Equal(TipoMovimiento.Adicional, nuevo.TipoMovimiento);

        // Rubros sin código de los ítems nuevos: raíz (el separador vació la pila).
        var limpieza = f.Single(x => x.Descripcion == "Limpieza, Retiro y Demoliciones");
        Assert.Equal(TipoFilaEstructura.Rubro, limpieza.Tipo);
        Assert.Null(limpieza.PadreIndice);
        Assert.Equal(f.IndexOf(limpieza), nuevo.PadreIndice);
        var artefactos = f.Single(x => x.Descripcion == "Intalación de Artefactos");
        Assert.Equal(TipoFilaEstructura.SubRubro, artefactos.Tipo);
        Assert.Equal(f.IndexOf(f.Single(x => x.Descripcion == "Instalación Eléctrica")), artefactos.PadreIndice);

        Assert.Equal(1344825550.45m, s.TotalPlanilla);
        Assert.Equal(3, s.CantidadItems);
    }

    [Fact]
    public void Balance_GrupoContrato_ImportaElContratoCompleto()
    {
        var s = ImportadorEstructura.Analizar(BalanceAguero(), GrupoBalance.Contrato).Secciones[0];

        var obrador = s.Filas.Single(x => x.Codigo == "PEV-GE.01.1");
        Assert.Equal(1m, obrador.Cantidad);
        Assert.Null(obrador.TipoMovimiento);
        Assert.Equal(TipoFilaEstructura.Item, s.Filas.Single(x => x.Codigo == "PEV-SD-EAG-01.10.8").Tipo);
        Assert.Equal(TipoFilaEstructura.Rubro, s.Filas.Single(x => x.Codigo == "PEV-SD-EAG-01.10").Tipo);
        Assert.Equal(4025136349.28m, s.TotalPlanilla);
    }

    [Fact]
    public void Balance_GrupoContratoSinCantidad_AvisaEnVezDeImportarSinMonto()
    {
        // Mismo balance sin la columna Cantidad del contrato en el encabezado: con el grupo
        // Contrato todos los ítems saldrían «sin monto» en silencio.
        var hoja = BalanceAguero(conCantidadContrato: false);

        var contrato = ImportadorEstructura.Analizar(hoja, GrupoBalance.Contrato);
        Assert.Equal(LayoutEstructura.Balance, contrato.Layout);
        Assert.Equal("El grupo «Contrato» no tiene la columna Cantidad en el encabezado.", contrato.Error);
        Assert.Empty(contrato.Secciones);

        // Los otros grupos no dependen de esa columna.
        Assert.Null(ImportadorEstructura.Analizar(hoja, GrupoBalance.Demasia).Error);
    }

    // ── Combinar secciones ───────────────────────────────────────────────────────

    [Fact]
    public void Combinar_CorreLosIndicesDePadre()
    {
        var hoja = Hoja(
            EncabezadoCotizacion,
            ["A", "Rubro A"],
            ["A.1", "Ítem A1", "u", 1, 10],
            [null, null, null, "TOTAL A =", 10],
            ["B", "Rubro B"],
            ["B.1", "Ítem B1", "u", 1, 20],
            [null, null, null, "TOTAL B =", 20]);
        var a = ImportadorEstructura.Analizar(hoja);

        var combinadas = ImportadorEstructura.Combinar(a.Secciones);

        Assert.Equal(4, combinadas.Count);
        Assert.Equal(0, combinadas[1].PadreIndice);
        Assert.Null(combinadas[2].PadreIndice);
        Assert.Equal(2, combinadas[3].PadreIndice);
        Assert.Equal("Ítem B1", combinadas[3].Descripcion);   // la copia conserva el resto de las propiedades
        Assert.Equal(20m, combinadas[3].Monto);
        // El original no se muta.
        Assert.Equal(0, a.Secciones[1].Filas[1].PadreIndice);
    }
}
