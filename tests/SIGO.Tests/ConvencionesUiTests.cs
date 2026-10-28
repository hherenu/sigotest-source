using System.Text.RegularExpressions;

namespace SIGO.Tests;

/// <summary>
/// Reglas de docs/convenciones-ui.md que se pueden verificar leyendo las páginas.
/// </summary>
public class ConvencionesUiTests
{
    private static string Componentes()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Sigotest.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "src", "Sigotest.Web", "Components");
    }

    [Fact]
    public void RadzenNumeric_SinMinNiMax()
    {
        // Radzen lleva el valor tipeado al límite sin avisar (un PU de -500 se guardaba
        // como 0): los límites van como regla del validador del módulo, evaluada con un
        // RadzenCustomValidator. El patrón se corta en el próximo «<» para no mirar otra etiqueta.
        var etiqueta = new Regex(@"<RadzenNumeric\b[^<]*?\s(Min|Max)\s*=");
        var infractores = Directory.EnumerateFiles(Componentes(), "*.razor", SearchOption.AllDirectories)
            .Where(f => etiqueta.IsMatch(File.ReadAllText(f)))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Empty(infractores);
    }

    [Fact]
    public void TextosQueSeGuardan_ConMaxLength()
    {
        // Sin MaxLength se puede tipear más de lo que entra en la columna (LargoTexto). El
        // pegado de índices no se guarda tal cual: se analiza fila por fila.
        var etiqueta = new Regex(@"<RadzenText(Box|Area)\b[^<]*?@bind-Value=""(?<campo>[^""]+)""[^<]*?/>");
        var sinLargo = Directory.EnumerateFiles(Componentes(), "*.razor", SearchOption.AllDirectories)
            .SelectMany(f => etiqueta.Matches(File.ReadAllText(f))
                .Where(m => !m.Value.Contains("MaxLength=") && m.Groups["campo"].Value != "textoPegado")
                .Select(m => $"{Path.GetFileName(f)}: {m.Groups["campo"].Value}"))
            .ToList();

        Assert.Empty(sinLargo);
    }
}
