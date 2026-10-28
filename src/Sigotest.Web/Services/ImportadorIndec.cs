using System.Globalization;
using System.Text.RegularExpressions;
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

            var textoValor = campos.Length switch { 2 => campos[1], 4 => campos[3], _ => null };
            if (textoValor is not null && ValorAmbiguo(textoValor) is string ambiguo)
            {
                f.Estado = EstadoFilaImport.FilaInvalida;
                f.Detalle = ambiguo;
                continue;
            }

            switch (campos.Length)
            {
                case 2 when TryValor(campos[1], out var v2):
                    Asignar(f, anioDefault, mesDefault, v2);
                    break;
                case 4 when int.TryParse(campos[1], out var a) && int.TryParse(campos[2], out var m)
                         && TryValor(campos[3], out var v4):
                    Asignar(f, a, m, v4);
                    break;
                default:
                    f.Estado = EstadoFilaImport.FilaInvalida;
                    f.Detalle = "Se esperan 2 columnas (código, valor) o 4 (código, año, mes, valor).";
                    break;
            }
        }
        return resultado;
    }

    /// <summary>
    /// Mismas reglas que el alta manual (IndiceValidator): período válido y valor mayor que
    /// cero, con su mensaje. El período por defecto (formato de 2 columnas) también pasa por
    /// acá: la página ya no lo acota. Un período inválido no se asigna a la fila: la grilla
    /// no podría mostrar un mes 13.
    /// </summary>
    private static void Asignar(FilaImport f, int anio, int mes, decimal valor)
    {
        f.Valor = valor;
        if (IndiceValidator.Periodo(anio, mes) is string periodoInvalido)
        {
            f.Estado = EstadoFilaImport.FilaInvalida;
            f.Detalle = periodoInvalido;
            return;
        }
        f.Anio = anio; f.Mes = mes;
        if (IndiceValidator.ValorMensual(valor) is string error)
        {
            f.Estado = EstadoFilaImport.FilaInvalida;
            f.Detalle = error;
        }
    }

    /// <summary>
    /// Con coma se interpreta es-AR ("10.819,3"); sin coma, invariante ("10819.3").
    /// Parsear "8834.3" como es-AR daría 88343 (punto = separador de miles). Los valores
    /// de índices revisan antes <see cref="ValorAmbiguo"/>.
    /// </summary>
    public static bool TryValor(string s, out decimal v) =>
        s.Contains(',')
            ? decimal.TryParse(s, NumberStyles.Number, CultureInfo.GetCultureInfo("es-AR"), out v)
            : decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out v);

    private static readonly Regex PuntosDeMilesSinComa = new(@"^\d{1,3}(\.\d{3})+$");

    /// <summary>
    /// «11.130» sin coma puede ser 11130 (punto de miles, como en el PDF) o 11,13 (punto
    /// decimal): se leía como 11,13 y la plantilla de Excel mostraba 11130. En los valores
    /// de índices (pegado y plantilla) se rechaza con el motivo; «11130», «11.130,0» y
    /// «8834.3» no son ambiguos. La plantilla aplica la misma regla en sus fórmulas
    /// (<see cref="Importacion.PlantillaIndices"/>). La importación de estructuras no la usa:
    /// avisa cualquier número tipeado como texto.
    /// </summary>
    public static string? ValorAmbiguo(string s) =>
        PuntosDeMilesSinComa.IsMatch(s)
            ? $"\"{s}\" es ambiguo: escribilo {s.Replace(".", "")} o {s},0"
            : null;
}
