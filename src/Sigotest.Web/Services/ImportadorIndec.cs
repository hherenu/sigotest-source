using System.Globalization;
using SIGO.Services.Validaciones;

namespace SIGO.Services;

/// <summary>Estado de una fila analizada del pegado de importación.</summary>
public enum EstadoFilaImport { Ok, YaExiste, CodigoDesconocido, DuplicadaEnPegado, FilaInvalida }

/// <summary>Fila del pegado de /indices/importar, con su resultado de análisis.</summary>
public sealed class FilaImport
{
    public int Linea { get; set; }
    public string Codigo { get; set; } = "";
    public int IndiceId { get; set; }
    public int Anio { get; set; }
    public int Mes { get; set; }
    public decimal? Valor { get; set; }
    public EstadoFilaImport Estado { get; set; } = EstadoFilaImport.Ok;
    public string? Detalle { get; set; }
}

/// <summary>
/// Parseo puro del pegado de publicaciones INDEC (sin tocar la base): formatos de
/// 2 columnas (código, valor — usa el período por defecto) y 4 columnas (código,
/// año, mes, valor), separados por tabulación o «;». Extraído de la página de
/// importación para poder testear las reglas de parseo — en particular la de
/// decimales, donde interpretar mal la cultura multiplica el valor por 10.000.
/// </summary>
public static class ImportadorIndec
{
    public static List<FilaImport> ParsearLineas(string? texto, int anioDefault, int mesDefault)
    {
        var resultado = new List<FilaImport>();
        if (string.IsNullOrWhiteSpace(texto)) return resultado;

        var lineas = texto.Split('\n');
        for (int i = 0; i < lineas.Length; i++)
        {
            var linea = lineas[i].Trim().TrimEnd('\r');
            if (linea.Length == 0) continue;

            var f = new FilaImport { Linea = i + 1 };
            resultado.Add(f);

            var campos = linea.Split(new[] { '\t', ';' }, StringSplitOptions.TrimEntries)
                              .Where(c => c.Length > 0).ToArray();
            f.Codigo = campos.Length > 0 ? campos[0] : "";

            switch (campos.Length)
            {
                case 2 when TryValor(campos[1], out var v2):
                    f.Anio = anioDefault; f.Mes = mesDefault; f.Valor = v2;
                    break;
                case 4 when int.TryParse(campos[1], out var a) && a is >= 2000 and <= 2100
                         && int.TryParse(campos[2], out var m) && m is >= 1 and <= 12
                         && TryValor(campos[3], out var v4):
                    f.Anio = a; f.Mes = m; f.Valor = v4;
                    break;
                default:
                    f.Estado = EstadoFilaImport.FilaInvalida;
                    f.Detalle = "Se esperan 2 columnas (código, valor) o 4 (código, año, mes, valor).";
                    break;
            }

            if (f.Estado == EstadoFilaImport.Ok && IndiceValidator.ValorMensual(f.Valor) is string error)
            {
                f.Estado = EstadoFilaImport.FilaInvalida;
                f.Detalle = error;
            }
        }
        return resultado;
    }

    /// <summary>
    /// Con coma se interpreta es-AR ("10.819,3"); sin coma, invariante ("10819.3").
    /// Parsear "8834.3" como es-AR daría 88343 (punto = separador de miles).
    /// </summary>
    public static bool TryValor(string s, out decimal v) =>
        s.Contains(',')
            ? decimal.TryParse(s, NumberStyles.Number, CultureInfo.GetCultureInfo("es-AR"), out v)
            : decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out v);
}
