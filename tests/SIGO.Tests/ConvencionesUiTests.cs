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
}
