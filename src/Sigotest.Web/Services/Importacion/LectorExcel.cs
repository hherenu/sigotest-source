using System.Globalization;
using System.Text;
using ExcelDataReader;
using ExcelDataReader.Exceptions;

namespace SIGO.Services.Importacion;

/// <summary>
/// Lectura de planillas subidas (.xls, .xlsx, .xlsm) a una grilla en memoria, con
/// ExcelDataReader. Se leen valores cacheados (las fórmulas valen lo que Excel guardó
/// al cerrar el archivo, que es lo que el usuario vio), los errores de Excel y si cada
/// hoja está oculta. Las columnas se acotan a <see cref="MaxColumnas"/> porque una hoja
/// con formato aplicado hasta la XFC declara 16.384 columnas y recorrerlas todas por
/// fila tarda minutos; las filas con datos, a <see cref="MaxFilasConDatos"/> por hoja.
/// El libro entero se acota a <see cref="MaxHojas"/> hojas y <see cref="MaxFilasRecorridas"/>
/// filas recorridas (las vacías también cuestan: una celda suelta en la fila 1.048.576
/// obliga a recorrer el millón anterior), y la lectura se puede cancelar: sin topes ni
/// cancelación, un archivo de pocos MB armado así podía ocupar CPU del servidor mucho
/// tiempo aunque el usuario ya se hubiera ido.
/// </summary>
public static class LectorExcel
{
    public const int MaxColumnas = 120;
    /// <summary>Las planillas reales no pasan de unas mil filas: el tope solo frena una hoja patológica.</summary>
    public const int MaxFilasConDatos = 50_000;
    /// <summary>Los libros reales traen decenas de hojas (copias de meses anteriores incluidas).</summary>
    public const int MaxHojas = 500;
    /// <summary>Filas recorridas entre todas las hojas, vacías incluidas: alcanza para una hoja con una celda en la última fila de Excel.</summary>
    public const int MaxFilasRecorridas = 2_000_000;
    public const int TamanoMaximoBytes = 12 * 1024 * 1024;
    public static readonly string[] ExtensionesAdmitidas = [".xls", ".xlsx", ".xlsm"];

    /// <summary>«.xls, .xlsx o .xlsm», para los textos de ayuda y de rechazo.</summary>
    public static string ExtensionesTexto =>
        $"{string.Join(", ", ExtensionesAdmitidas[..^1])} o {ExtensionesAdmitidas[^1]}";

    private static readonly CultureInfo EsAr = CultureInfo.GetCultureInfo("es-AR");
    private const double BytesPorMb = 1024 * 1024;

    static LectorExcel()
    {
        // Los .xls (BIFF) traen textos en cp1252: en .NET (Core) el proveedor de code
        // pages no está registrado por defecto y ExcelDataReader falla al abrirlos.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// Mensaje de rechazo por extensión o por tamaño, o null si el archivo es admisible.
    /// El tamaño lo informa el navegador: al leer se vuelve a exigir en el servidor.
    /// </summary>
    public static string? ValidarArchivo(string? nombreArchivo, long tamanoBytes)
    {
        var ext = Path.GetExtension(nombreArchivo ?? string.Empty).ToLowerInvariant();
        if (!ExtensionesAdmitidas.Contains(ext))
            return $"Solo se admiten planillas Excel ({ExtensionesTexto}).";
        if (tamanoBytes > TamanoMaximoBytes)
        {
            // Redondeado hacia arriba: 12,04 MB no puede decir «pesa 12 MB y el máximo es 12 MB».
            var mb = Math.Ceiling(tamanoBytes * 10 / BytesPorMb) / 10;
            return $"La planilla pesa {mb.ToString("0.#", EsAr)} MB y el máximo es {DescribirTamano(TamanoMaximoBytes)}. {SugerenciaCopia}";
        }
        return null;
    }

    /// <summary>Tamaño para mostrar: «850 KB», «2,5 MB».</summary>
    public static string DescribirTamano(long bytes) =>
        bytes < BytesPorMb
            ? $"{Math.Max(1, (long)Math.Round(bytes / 1024d))} KB"
            : $"{(bytes / BytesPorMb).ToString("0.#", EsAr)} MB";

    /// <summary>
    /// Lee todas las hojas del libro. Lanza InvalidOperationException con un mensaje
    /// para el usuario si el archivo no es una planilla o está protegido.
    /// </summary>
    public static LibroExcel Leer(byte[] contenido, CancellationToken cancelacion = default) =>
        Leer(new MemoryStream(contenido, writable: false), cancelacion);

    /// <summary>
    /// Igual que <see cref="Leer(byte[], CancellationToken)"/> sobre un stream con seek
    /// (ExcelDataReader lo necesita para los .xlsx y los .xls). Lee desde el principio.
    /// Cancelado, lanza OperationCanceledException. Los topes son parámetros para que los
    /// tests los ejerciten con libros chicos; la app usa siempre los de la clase.
    /// </summary>
    public static LibroExcel Leer(Stream contenido, CancellationToken cancelacion = default,
        int maxHojas = MaxHojas, int maxFilasRecorridas = MaxFilasRecorridas)
    {
        if (contenido.Length == 0)
            throw new InvalidOperationException("El archivo está vacío.");

        try
        {
            contenido.Position = 0;
            using var reader = ExcelReaderFactory.CreateReader(contenido);
            var hojas = new List<HojaExcel>();
            var recorridas = 0;
            do
            {
                cancelacion.ThrowIfCancellationRequested();
                if (hojas.Count == maxHojas)
                    throw new InvalidOperationException(
                        $"La planilla tiene más de {maxHojas.ToString("N0", EsAr)} hojas. {SugerenciaCopia}");
                hojas.Add(LeerHoja(reader, hojas.Count + 1, ref recorridas, maxFilasRecorridas, cancelacion));
            } while (reader.NextResult());
            return new LibroExcel(hojas);
        }
        catch (InvalidPasswordException)
        {
            throw new InvalidOperationException("La planilla está protegida con contraseña y no se puede leer.");
        }
        catch (HeaderException)
        {
            throw new InvalidOperationException("El archivo no es una planilla Excel válida.");
        }
        catch (ExcelReaderException ex)
        {
            throw new InvalidOperationException($"No se pudo leer la planilla: {ex.Message}");
        }
        // Un .xlsx truncado o con el zip dañado sale como InvalidDataException del
        // ZipArchive, y algunos .xls raros con otras excepciones del lector: para el
        // usuario todas son «archivo ilegible», no un error del circuito. La cancelación
        // sigue siendo cancelación (el selector la descarta sin avisar).
        catch (Exception ex) when (ex is not InvalidOperationException and not OperationCanceledException)
        {
            throw new InvalidOperationException($"No se pudo leer la planilla: el archivo está dañado o no es un Excel válido ({ex.GetType().Name}).", ex);
        }
    }

    private const string SugerenciaCopia = "Guardá una copia solo con las hojas que vas a importar y subí esa.";

    private static HojaExcel LeerHoja(IExcelDataReader reader, int indice,
        ref int recorridas, int maxFilasRecorridas, CancellationToken cancelacion)
    {
        var filas = new List<FilaExcel>();
        var columnas = Math.Min(reader.FieldCount, MaxColumnas);
        var numero = 0;
        var recortada = false;
        while (reader.Read())
        {
            numero++;
            if (++recorridas > maxFilasRecorridas)
                throw new InvalidOperationException(
                    $"La planilla es demasiado grande para leerla: entre todas sus hojas pasa de {maxFilasRecorridas.ToString("N0", EsAr)} filas " +
                    $"(suele deberse a celdas usadas muy por debajo de los datos). {SugerenciaCopia}");
            if (recorridas % 1024 == 0) cancelacion.ThrowIfCancellationRequested();
            CeldaExcel[]? celdas = null;
            var ultima = -1;
            for (var c = 0; c < columnas; c++)
            {
                // Un error de Excel llega como null: se conserva para que no pase por vacío
                // (un «#REF!» en el % del mes no es 0 %).
                var valor = reader.GetValue(c) ?? (reader.GetCellError(c) is CellError error ? new ErrorExcel(TextoDeError(error)) : null);
                if (valor is null || (valor is string s && s.Length == 0)) continue;
                celdas ??= new CeldaExcel[columnas];
                celdas[c] = new CeldaExcel(valor, reader.GetNumberFormatString(c));
                ultima = c;
            }
            if (celdas is null) continue;
            if (filas.Count == MaxFilasConDatos) { recortada = true; break; }

            var recortadas = new CeldaExcel[ultima + 1];
            for (var c = 0; c <= ultima; c++)
                recortadas[c] = celdas[c] ?? CeldaExcel.Vacia;
            filas.Add(new FilaExcel(numero, recortadas));
        }
        // VisibleState: "visible", "hidden" o "veryhidden" (oculta por macro).
        var oculta = reader.VisibleState is string estado && !estado.Equals("visible", StringComparison.OrdinalIgnoreCase);
        return new HojaExcel(reader.Name ?? $"Hoja {indice}", filas, oculta, recortada);
    }

    private static string TextoDeError(CellError error) => error switch
    {
        CellError.NULL => "#NULL!",
        CellError.DIV0 => "#DIV/0!",
        CellError.VALUE => "#VALUE!",
        CellError.REF => "#REF!",
        CellError.NAME => "#NAME?",
        CellError.NUM => "#NUM!",
        CellError.NA => "#N/A",
        _ => "#ERROR"
    };
}
