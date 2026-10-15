using ClosedXML.Excel;
using SIGO.Models.ViewModels;
using SIGO.Services.Validaciones;

namespace SIGO.Services.Importacion;

/// <summary>
/// Plantilla completada y leída: la publicación que trae, sus valores como filas del
/// pipeline de importación, o el motivo por el que no se puede usar.
/// </summary>
public sealed record LecturaPlantillaIndices(string? IdPublicacion, List<FilaImport> Filas, string? Error = null);

/// <summary>
/// Plantilla Excel de valores mensuales de índices (/indices/importar), con el formato del
/// Excel maestro provisorio: una fila por índice del catálogo vigente, una celda donde se
/// pega la fila de valores tal como sale del PDF de la revista y 13 columnas de mes que la
/// reparten con fórmulas, para que el usuario vea cada valor en su mes. Un número tipeado
/// en la celda de un mes reemplaza su fórmula y tiene prioridad.
/// La lectura NO depende de las fórmulas: reparte el pegado con la misma regla
/// (<see cref="RepartirPegado"/>), así que un archivo guardado sin recalcular o abierto en
/// otro programa da lo mismo. Las filas que produce son las del pegado de texto
/// (<see cref="FilaImport"/>): el análisis contra el catálogo y la base, la vista previa y
/// la importación no cambian. ÚNICA definición del formato: Crear y Leer comparten hojas,
/// rótulos y marca; la lectura los ubica por su rótulo, no por posición.
/// </summary>
public static class PlantillaIndices
{
    public const string HojaCarga = "Carga";
    /// <summary>Meses de la revista INDEC Informa: el último publicado y los 12 anteriores.</summary>
    public const int CantidadMeses = 13;

    // Marca en una hoja muy oculta (no se puede mostrar desde Excel): distingue la plantilla
    // de cualquier otro libro con una columna «Código».
    private const string HojaMarca = "SIGO";
    private const string Marca = "SIGO_PLANTILLA_INDICES";
    private const int Version = 1;

    private const string RotuloPublicacion = "ID publicación";
    private const string RotuloCodigo = "CÓDIGO";
    private const string RotuloPegado = "Pegado del PDF (fila de valores)";
    private static readonly string[] ColumnasDelCatalogo =
        [RotuloCodigo, "Familia de recurso", "Índice normalizado", "Cuadro", "Inciso"];
    private const string RotuloNormalizado = "aux normalizado";
    private const string RotuloSinBase = "aux sin base";
    private static readonly string[] RotulosConocidos = [.. ColumnasDelCatalogo, RotuloPegado, RotuloNormalizado, RotuloSinBase];
    /// <summary>Valor índice de la base: si se copió la fila entera del PDF, lo anterior se descarta.</summary>
    private const string Base = "100,0";

    private const int FilaEncabezado = 4;
    private static readonly int ColPegado = ColumnasDelCatalogo.Length + 1;
    private static readonly int ColPrimerMes = ColPegado + 1;
    private static readonly int ColNormalizado = ColPrimerMes + CantidadMeses;   // auxiliar oculta
    private static readonly int ColSinBase = ColNormalizado + 1;                 // auxiliar oculta
    private const string FormatoMes = "mmm-yy";
    private const string FormatoValor = "#,##0.0###";

    /// <summary>
    /// Meses de la plantilla de una publicación: los 13 que muestra la revista, del mismo
    /// mes del año anterior al mes previo a la publicación (07/26 → jun-25 … jun-26).
    /// </summary>
    public static IReadOnlyList<DateTime> Meses(int anioPublicacion, int mesPublicacion)
    {
        var ultimo = new DateTime(anioPublicacion, mesPublicacion, 1).AddMonths(-1);
        return Enumerable.Range(1 - CantidadMeses, CantidadMeses).Select(ultimo.AddMonths).ToList();
    }

    /// <summary>
    /// Reparte la fila de valores pegada del PDF en sus meses, en orden: separa por espacios
    /// (también tabulaciones, saltos de línea y espacios duros), descarta lo anterior a la
    /// base «100,0» si se copió la fila entera, y una «s» (secreto estadístico) deja el mes
    /// vacío. Los números siguen la regla del pegado de texto (<see cref="ImportadorIndec.TryValor"/>).
    /// La cantidad de valores tiene que coincidir con la de meses: con uno de más o de menos
    /// cada valor caería en el mes equivocado sin que nada lo muestre.
    /// </summary>
    public static (IReadOnlyList<decimal?> Valores, string? Error) RepartirPegado(string? pegado, int meses)
    {
        var tokens = (pegado ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var base100 = Array.IndexOf(tokens, Base);
        if (base100 >= 0) tokens = tokens[(base100 + 1)..];

        if (tokens.Length != meses)
            return ([], $"El pegado tiene {tokens.Length} {(tokens.Length == 1 ? "valor" : "valores")} y la plantilla {meses} meses: copiá la fila del PDF " +
                        $"desde el primer valor después del «{Base}» hasta el último mes.");

        var valores = new decimal?[meses];
        for (var i = 0; i < meses; i++)
        {
            if (tokens[i].Equals("s", StringComparison.OrdinalIgnoreCase)) continue;
            if (!ImportadorIndec.TryValor(tokens[i], out var valor))
                return ([], $"«{tokens[i]}» del pegado no es un valor: copiá solo la fila de valores del PDF.");
            valores[i] = valor;
        }
        return (valores, null);
    }

    /// <summary>
    /// Libro .xlsx con una fila por índice del catálogo, la columna del pegado y una columna
    /// por mes con las fórmulas que lo reparten. Las tasas del Banco Nación no van: se cargan
    /// sin publicación (<see cref="IndiceService.CodigoTasasBancoNacion"/>). La publicación
    /// tiene que tener el formato de la importación.
    /// </summary>
    public static byte[] Crear(string? idPublicacion, IReadOnlyList<IndiceVM> catalogo)
    {
        Validacion.Exigir(IndiceValidator.PublicacionImportacion(idPublicacion));
        var id = idPublicacion!.Trim();
        PublicacionIndec.TryPeriodo(id, out var anio, out var mes);
        var meses = Meses(anio, mes);
        var indices = catalogo.Where(i => !IndiceService.EsTasasBancoNacion(i.CodigoIndice)).ToList();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(HojaCarga);
        var ultimaFila = FilaEncabezado + indices.Count;
        var letraPegado = XLHelper.GetColumnLetterFromNumber(ColPegado);
        var letraNormalizado = XLHelper.GetColumnLetterFromNumber(ColNormalizado);
        var letraSinBase = XLHelper.GetColumnLetterFromNumber(ColSinBase);
        var rangoMeses = $"{XLHelper.GetColumnLetterFromNumber(ColPrimerMes)}{FilaEncabezado + 1}:" +
                         $"{XLHelper.GetColumnLetterFromNumber(ColPrimerMes + CantidadMeses - 1)}{Math.Max(ultimaFila, FilaEncabezado + 1)}";

        ws.Cell(1, 1).Value = RotuloPublicacion;
        ws.Cell(1, 2).Value = id;
        ws.Range(1, 1, 1, 2).Style.Font.Bold = true;
        ws.Cell(1, 4).Value = "Valores en los meses:";
        ws.Cell(1, 5).FormulaA1 = $"COUNT({rangoMeses})";
        ws.Cell(2, 1).Value = $"Pegá en «{RotuloPegado}» la fila de valores de cada índice tal como sale del PDF, desde el " +
                              $"primer valor después del «{Base}» hasta el último mes: las columnas de mes se completan solas y una «s» deja el mes vacío.";
        ws.Cell(3, 1).Value = "Un número tipeado en la celda de un mes reemplaza su fórmula y tiene prioridad. Después subí este archivo en Importar publicación INDEC.";
        ws.Range(2, 1, 3, 1).Style.Font.Italic = true;

        for (var c = 0; c < ColumnasDelCatalogo.Length; c++)
            ws.Cell(FilaEncabezado, c + 1).Value = ColumnasDelCatalogo[c];
        ws.Cell(FilaEncabezado, ColPegado).Value = RotuloPegado;
        for (var i = 0; i < meses.Count; i++)
        {
            var celda = ws.Cell(FilaEncabezado, ColPrimerMes + i);
            celda.Value = meses[i];
            celda.Style.DateFormat.Format = FormatoMes;
            celda.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
        ws.Cell(FilaEncabezado, ColNormalizado).Value = RotuloNormalizado;
        ws.Cell(FilaEncabezado, ColSinBase).Value = RotuloSinBase;
        var encabezado = ws.Range(FilaEncabezado, 1, FilaEncabezado, ColPrimerMes + CantidadMeses - 1);
        encabezado.Style.Font.Bold = true;
        encabezado.Style.Fill.BackgroundColor = XLColor.FromHtml("#E6E6E6");

        for (var r = 0; r < indices.Count; r++)
        {
            var fila = FilaEncabezado + 1 + r;
            var indice = indices[r];
            ws.Cell(fila, 1).Value = indice.CodigoIndice;
            ws.Cell(fila, 2).Value = indice.FamiliaRecurso;
            ws.Cell(fila, 3).Value = indice.IndiceNormalizado ?? "";
            ws.Cell(fila, 4).Value = indice.CuadroReferencia ?? "";
            ws.Cell(fila, 5).Value = indice.IncisoCode ?? "";

            // Mismas reglas que RepartirPegado: espacios de cualquier tipo → uno, sin lo anterior
            // a la base y un valor por mes. NUMBERVALUE con la coma como decimal no depende del
            // idioma de Excel (VALOR("8.943,3") solo anda en castellano); una «s» o un faltante
            // dejan el mes vacío.
            ws.Cell(fila, ColNormalizado).FormulaA1 =
                $"TRIM(SUBSTITUTE(SUBSTITUTE(SUBSTITUTE(SUBSTITUTE({letraPegado}{fila},CHAR(9),\" \"),CHAR(160),\" \"),CHAR(10),\" \"),CHAR(13),\" \"))";
            var texto = $"\" \"&{letraNormalizado}{fila}&\" \"";
            ws.Cell(fila, ColSinBase).FormulaA1 =
                $"IF(ISNUMBER(SEARCH(\" {Base} \",{texto})),TRIM(MID({texto},SEARCH(\" {Base} \",{texto})+{Base.Length + 2},9999)),{letraNormalizado}{fila})";
            for (var k = 0; k < CantidadMeses; k++)
            {
                var token = $"TRIM(MID(SUBSTITUTE(${letraSinBase}{fila},\" \",REPT(\" \",300)),{k * 300 + 1},300))";
                ws.Cell(fila, ColPrimerMes + k).FormulaA1 =
                    $"IFERROR(IF({token}=\"\",\"\",_xlfn.NUMBERVALUE({token},\",\",\".\")),\"\")";
            }
        }

        if (indices.Count > 0)
        {
            var pegado = ws.Range(FilaEncabezado + 1, ColPegado, ultimaFila, ColPegado);
            pegado.Style.NumberFormat.Format = "@";   // texto: un valor suelto pegado no se convierte en número
            pegado.Style.Fill.BackgroundColor = XLColor.LightYellow;

            var valores = ws.Range(rangoMeses);
            valores.Style.NumberFormat.Format = FormatoValor;
            valores.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            valores.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            valores.Style.Border.InsideBorderColor = XLColor.FromHtml("#BFBFBF");
            valores.Style.Border.OutsideBorderColor = XLColor.FromHtml("#BFBFBF");
            // Un valor tipeado sobre la fórmula sigue la regla de la importación
            // (IndiceValidator.ValorMensual). Pegar la saltea: por eso la lectura la vuelve a exigir.
            var validacion = valores.CreateDataValidation();
            validacion.Decimal.GreaterThan(0);
            validacion.ErrorTitle = "Valor inválido";
            validacion.ErrorMessage = "El valor de un mes tiene que ser un número mayor que cero.";
        }

        ws.Column(1).Width = 30;
        ws.Column(2).Width = 40;
        ws.Column(3).Width = 34;
        ws.Column(4).Width = 14;
        ws.Column(5).Width = 8;
        ws.Column(ColPegado).Width = 40;
        for (var k = 0; k < CantidadMeses; k++) ws.Column(ColPrimerMes + k).Width = 10;
        ws.Column(ColNormalizado).Hide();
        ws.Column(ColSinBase).Hide();
        ws.SheetView.Freeze(FilaEncabezado, 1);

        var marca = wb.Worksheets.Add(HojaMarca);
        marca.Cell(1, 1).Value = Marca;
        marca.Cell(1, 2).Value = Version;
        marca.Visibility = XLWorksheetVisibility.VeryHidden;

        using var salida = new MemoryStream();
        wb.SaveAs(salida);
        return salida.ToArray();
    }

    /// <summary>
    /// Lee una plantilla completada. Por cada índice, el valor de cada mes es el número de su
    /// celda si tiene uno (tipeado, o el que calculó Excel con la fórmula) y, si está vacía,
    /// el que sale del pegado. Celdas y pegado vacíos no se importan. Un pegado que no se
    /// puede repartir, un texto, un error de Excel o un valor que no es mayor que cero quedan
    /// como «Fila inválida» con el motivo, igual que en el pegado de texto.
    /// </summary>
    public static LecturaPlantillaIndices Leer(LibroExcel libro)
    {
        const string Descargar = "Descargá la plantilla de nuevo desde esta página.";
        if (!TieneMarca(libro))
            return Rechazo($"El archivo no es la plantilla de índices de SIGO. {Descargar}");

        var hoja = libro.Hojas.FirstOrDefault(h => Igual(h.Nombre, HojaCarga));
        if (hoja is null)
            return Rechazo($"La plantilla no tiene la hoja «{HojaCarga}». {Descargar}");

        string? idPublicacion = null;
        var indiceEncabezado = -1;
        var limite = Math.Min(hoja.Filas.Count, TextoImport.FilasDeBusquedaDeEncabezado);
        for (var i = 0; i < limite && indiceEncabezado < 0; i++)
        {
            var celdas = hoja.Filas[i].Celdas;
            for (var c = 0; c < celdas.Count; c++)
            {
                // El ID va en la celda contigua al rótulo: vacía es «sin ID», no la siguiente con texto.
                if (idPublicacion is null && Igual(celdas[c].Texto, RotuloPublicacion))
                    idPublicacion = hoja.Filas[i][c + 1].Texto;
                else if (Igual(celdas[c].Texto, RotuloCodigo))
                {
                    indiceEncabezado = i;
                    break;
                }
            }
        }
        if (indiceEncabezado < 0)
            return Rechazo($"No se encontró el encabezado de la plantilla (columna «{RotuloCodigo}»). {Descargar}");
        if (string.IsNullOrWhiteSpace(idPublicacion))
            return Rechazo($"La plantilla no tiene el ID de publicación (celda «{RotuloPublicacion}»). {Descargar}");
        // Con la planilla cargada el ID no se edita en la página: se corrige en el archivo.
        if (IndiceValidator.PublicacionImportacion(idPublicacion) is string idInvalido)
            return Rechazo($"{idInvalido} Corregilo en la celda «{RotuloPublicacion}» de la planilla.");

        var encabezado = hoja.Filas[indiceEncabezado].Celdas;
        var colCodigo = Columna(encabezado, RotuloCodigo);
        var colPegado = Columna(encabezado, RotuloPegado);
        if (colPegado < 0)
            return Rechazo($"Falta la columna «{RotuloPegado}». {Descargar}");

        var meses = encabezado
            .Select((celda, col) => (Col: col, Fecha: celda.Valor as DateTime?))
            .Where(m => m.Fecha is not null)
            .Select(m => (m.Col, Anio: m.Fecha!.Value.Year, Mes: m.Fecha.Value.Month))
            .ToList();
        if (meses.Count == 0)
            return Rechazo($"El encabezado no tiene columnas de mes (fechas). {Descargar}");
        if (meses.GroupBy(m => (m.Anio, m.Mes)).FirstOrDefault(g => g.Count() > 1) is { } repetido)
            return Rechazo($"El mes {repetido.Key.Mes:00}/{repetido.Key.Anio} está en dos columnas: dejá una sola.");

        var datos = hoja.Filas.Skip(indiceEncabezado + 1).ToList();
        if (datos.FirstOrDefault(f => Igual(f[colCodigo].Texto, RotuloCodigo)) is { } otroEncabezado)
            return Rechazo($"La hoja tiene un segundo encabezado en la fila {otroEncabezado.Numero}: subí una publicación por archivo.");

        // Una columna con números y sin fecha en el encabezado es un mes cuyo rótulo se borró
        // o se pisó: sus valores se perderían sin aviso. Las columnas conocidas no cuentan.
        var conocidas = meses.Select(m => m.Col)
            .Concat(Enumerable.Range(0, encabezado.Count).Where(c => RotulosConocidos.Any(r => Igual(encabezado[c].Texto, r))))
            .ToHashSet();
        foreach (var fila in datos)
            for (var c = 0; c < fila.Celdas.Count; c++)
                if (!conocidas.Contains(c) && fila[c].Valor is double)
                    return Rechazo($"La fila {fila.Numero} tiene un número en una columna cuyo encabezado no es un mes. " +
                                   "Revisá que no se haya borrado la fecha del encabezado.");

        var filas = new List<FilaImport>();
        foreach (var fila in datos)
        {
            var codigo = fila[colCodigo].Texto;
            var pegado = fila[colPegado].Texto;
            var celdas = meses.Select(m => fila[m.Col]).ToList();
            var tieneValores = pegado.Length > 0 || celdas.Any(c => !c.EstaVacia);
            if (!tieneValores) continue;
            if (codigo.Length == 0)
            {
                filas.Add(Invalida(fila.Numero, "", "La fila tiene valores pero no tiene código."));
                continue;
            }
            // La plantilla no la trae; agregada a mano, no se importa con publicación.
            if (IndiceService.EsTasasBancoNacion(codigo))
            {
                filas.Add(Invalida(fila.Numero, codigo,
                    "No se importa con la plantilla: las tasas del Banco Nación se cargan sin publicación desde «Valores mensuales» del índice."));
                continue;
            }

            IReadOnlyList<decimal?>? delPegado = null;
            if (pegado.Length > 0)
            {
                var (valores, error) = RepartirPegado(pegado, meses.Count);
                if (error is not null)
                {
                    filas.Add(Invalida(fila.Numero, codigo, error));
                    continue;
                }
                delPegado = valores;
            }

            for (var k = 0; k < meses.Count; k++)
            {
                var (_, anio, mes) = meses[k];
                if (!celdas[k].EstaVacia)
                    filas.Add(DeCelda(fila.Numero, codigo, anio, mes, celdas[k]));
                else if (delPegado?[k] is decimal valor)
                    filas.Add(ConValor(fila.Numero, codigo, anio, mes, valor));
            }
        }
        return new LecturaPlantillaIndices(idPublicacion.Trim(), filas);
    }

    private static FilaImport DeCelda(int linea, string codigo, int anio, int mes, CeldaExcel celda) =>
        celda.Error is string excel
            ? Invalida(linea, codigo, $"La celda tiene un error de Excel ({excel}).", anio, mes)
            : celda.Numero is decimal valor
                ? ConValor(linea, codigo, anio, mes, valor)
                : Invalida(linea, codigo, $"«{celda.Texto}» no es un número.", anio, mes);

    private static FilaImport ConValor(int linea, string codigo, int anio, int mes, decimal valor)
    {
        var f = new FilaImport { Linea = linea, Codigo = codigo, Anio = anio, Mes = mes, Valor = valor };
        if (IndiceValidator.ValorMensual(valor) is string error)
        {
            f.Estado = EstadoFilaImport.FilaInvalida;
            f.Detalle = error;
        }
        return f;
    }

    private static FilaImport Invalida(int linea, string codigo, string motivo, int anio = 0, int mes = 0) => new()
    {
        Linea = linea, Codigo = codigo, Anio = anio, Mes = mes,
        Estado = EstadoFilaImport.FilaInvalida, Detalle = motivo
    };

    private static int Columna(IReadOnlyList<CeldaExcel> encabezado, string rotulo)
    {
        for (var c = 0; c < encabezado.Count; c++)
            if (Igual(encabezado[c].Texto, rotulo)) return c;
        return -1;
    }

    private static bool TieneMarca(LibroExcel libro) =>
        libro.Hojas.FirstOrDefault(h => Igual(h.Nombre, HojaMarca)) is { Filas.Count: > 0 } hoja
        && hoja.Filas[0][0].Texto == Marca
        && hoja.Filas[0][1].Numero == Version;

    private static bool Igual(string? a, string b) => TextoImport.Normalizar(a) == TextoImport.Normalizar(b);

    private static LecturaPlantillaIndices Rechazo(string motivo) => new(null, [], motivo);
}
