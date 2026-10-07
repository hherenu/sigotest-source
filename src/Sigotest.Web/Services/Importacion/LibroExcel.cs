using System.Globalization;

namespace SIGO.Services.Importacion;

/// <summary>
/// Celda de una planilla subida: el valor crudo tal como lo entrega el lector (double
/// para todo número, string, DateTime, bool o <see cref="ErrorExcel"/>) más el formato
/// numérico de la celda, que es lo único que permite saber si un 0,1 significa 10 %
/// (formato con «%») o 0,1.
/// </summary>
public sealed class CeldaExcel
{
    public static readonly CeldaExcel Vacia = new(null, null);

    public CeldaExcel(object? valor, string? formato)
    {
        Valor = valor;
        Formato = formato;
    }

    public object? Valor { get; }
    public string? Formato { get; }

    public bool EstaVacia => Valor is null || (Valor is string s && string.IsNullOrWhiteSpace(s));

    /// <summary>
    /// Texto recortado. Los números van con punto decimal (invariante) y con los
    /// decimales fijos del formato de la celda, si lo tiene: un código tipeado como
    /// número 1,10 con formato «0.00» debe leerse «1.10», no «1.1». Un error de Excel no
    /// tiene texto: «#DIV/0!» en la columna Item no es un código ni un título.
    /// </summary>
    public string Texto => Valor switch
    {
        null or ErrorExcel => string.Empty,
        string s => s.Trim(),
        double d => FormatearNumero(d, Formato),
        DateTime f => f.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        _ => Valor.ToString()?.Trim() ?? string.Empty
    };

    /// <summary>true si la celda es texto (un número tipeado como texto se parsea, pero conviene avisarlo).</summary>
    public bool EsTexto => Valor is string;

    /// <summary>
    /// Código del error de Excel de la celda («#REF!», «#DIV/0!»…), o null. Sin texto ni
    /// número, pero la celda no está vacía: un «#REF!» en el % del mes no es 0 %.
    /// </summary>
    public string? Error => (Valor as ErrorExcel)?.Codigo;

    private static string FormatearNumero(double d, string? formato)
    {
        var decimales = DecimalesFijos(formato);
        return decimales is int n
            ? d.ToString("F" + n, CultureInfo.InvariantCulture)
            : d.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Cantidad de ceros tras el punto en el formato («#,##0.00» → 2); null si no fija decimales.</summary>
    private static int? DecimalesFijos(string? formato)
    {
        if (string.IsNullOrEmpty(formato)) return null;
        var punto = formato.IndexOf('.');
        if (punto < 0) return null;
        var n = 0;
        for (var i = punto + 1; i < formato.Length && formato[i] == '0'; i++) n++;
        return n > 0 ? n : null;
    }

    /// <summary>
    /// Valor numérico: el double del lector, o un texto con la misma regla de cultura que
    /// el pegado INDEC (coma = es-AR, sin coma = invariante). Null si no es número.
    /// </summary>
    public decimal? Numero
    {
        get
        {
            switch (Valor)
            {
                case double d:
                    if (double.IsNaN(d) || double.IsInfinity(d)) return null;
                    try { return (decimal)d; } catch (OverflowException) { return null; }
                case string s when !string.IsNullOrWhiteSpace(s):
                    // «12,5%» tipeado como texto: el signo se descarta y el valor queda en puntos
                    // (EsPorcentaje es false para los textos, aunque la celda tenga formato %).
                    var limpio = s.Trim().TrimEnd('%').Trim();
                    return ImportadorIndec.TryValor(limpio, out var v) ? v : null;
                default: return null;
            }
        }
    }

    /// <summary>
    /// true si la celda es un número con formato de porcentaje: el valor crudo es una
    /// fracción. No cuentan un texto en una celda con ese formato («0.5%» tipeado con punto,
    /// que Excel es-AR no convierte: ya viene en puntos) ni un «%» literal del formato
    /// (<c>0.00"%"</c> o <c>0.00\%</c>), que Excel muestra sin multiplicar por 100.
    /// </summary>
    public bool EsPorcentaje => Valor is not string && Formato is not null && TienePorcentajeQueEscala(Formato);

    private static bool TienePorcentajeQueEscala(string formato)
    {
        var entreComillas = false;
        for (var i = 0; i < formato.Length; i++)
        {
            var c = formato[i];
            if (c == '"') entreComillas = !entreComillas;
            else if (entreComillas) continue;
            else if (c == '\\') i++;          // el carácter siguiente es literal
            else if (c == '%') return true;
        }
        return false;
    }
}

/// <summary>Error de Excel guardado en la celda («#REF!», «#DIV/0!»…), para no confundirlo con una celda vacía.</summary>
public sealed record ErrorExcel(string Codigo);

/// <summary>Fila con su número de Excel (1-based) y sus celdas por índice de columna (0-based).</summary>
public sealed class FilaExcel
{
    public FilaExcel(int numero, IReadOnlyList<CeldaExcel> celdas)
    {
        Numero = numero;
        Celdas = celdas;
    }

    public int Numero { get; }
    public IReadOnlyList<CeldaExcel> Celdas { get; }

    /// <summary>Celda por columna (0-based); fuera de rango devuelve la celda vacía.</summary>
    public CeldaExcel this[int columna] =>
        columna >= 0 && columna < Celdas.Count ? Celdas[columna] : CeldaExcel.Vacia;
}

/// <summary>Hoja leída: solo las filas con algún contenido, con su número original.</summary>
public sealed class HojaExcel
{
    public HojaExcel(string nombre, IReadOnlyList<FilaExcel> filas, bool oculta = false, bool recortada = false)
    {
        Nombre = nombre;
        Filas = filas;
        Oculta = oculta;
        Recortada = recortada;
    }

    public string Nombre { get; }
    public IReadOnlyList<FilaExcel> Filas { get; }

    /// <summary>
    /// Oculta en el libro. Las planillas reales esconden copias de meses anteriores con el
    /// mismo encabezado (Agüero, certificados N°15 y N°16): no se preseleccionan.
    /// </summary>
    public bool Oculta { get; }

    /// <summary>Se leyeron solo las primeras <see cref="LectorExcel.MaxFilasConDatos"/> filas con datos.</summary>
    public bool Recortada { get; }
}

public sealed class LibroExcel
{
    public LibroExcel(IReadOnlyList<HojaExcel> hojas) => Hojas = hojas;
    public IReadOnlyList<HojaExcel> Hojas { get; }
}
