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

    // Etiqueta de apertura completa: los valores entre comillas pueden tener «>» (lambdas,
    // tipos genéricos en CellRender).
    private static readonly Regex Grilla = new(@"<RadzenDataGrid\b(?:[^>""]|""[^""]*"")*>");

    private static List<(string Archivo, string Etiqueta, string Pagina)> Grillas()
    {
        var grillas = Directory.EnumerateFiles(Componentes(), "*.razor", SearchOption.AllDirectories)
            .Select(f => (Archivo: Path.GetFileName(f), Pagina: File.ReadAllText(f)))
            .SelectMany(p => Grilla.Matches(p.Pagina).Select(m => (p.Archivo, m.Value, p.Pagina)))
            .ToList();

        // Que el patrón no se haya salteado ninguna grilla.
        var aperturas = Directory.EnumerateFiles(Componentes(), "*.razor", SearchOption.AllDirectories)
            .Sum(f => Regex.Matches(File.ReadAllText(f), @"<RadzenDataGrid\b").Count);
        Assert.NotEmpty(grillas);
        Assert.Equal(aperturas, grillas.Count);
        return grillas;
    }

    [Fact]
    public void RadzenDataGrid_ConColumnasRedimensionables()
    {
        // Además de dejar ajustar los anchos: sin AllowColumnResize, Radzen borra filtros y
        // orden cada vez que recibe otra Data (una recarga tras una baja, la búsqueda rápida).
        var infractores = Grillas()
            .Where(g => !g.Etiqueta.Contains(@"AllowColumnResize=""true"""))
            .Select(g => g.Archivo)
            .ToList();

        Assert.Empty(infractores);
    }

    [Fact]
    public void GrillasConFiltro_ConLosTextosCompartidos()
    {
        // Los textos por defecto del popup de filtro de Radzen están en inglés.
        var infractores = Grillas()
            .Where(g => g.Etiqueta.Contains(@"AllowFiltering=""true"""))
            .Where(g => !g.Etiqueta.Contains(@"ApplyFilterText=""@GridTextos.AplicarFiltro""")
                        || !g.Etiqueta.Contains(@"ClearFilterText=""@GridTextos.LimpiarFiltro""")
                        || !g.Etiqueta.Contains(@"EmptyText=""@GridTextos."))
            .Select(g => g.Archivo)
            .ToList();

        Assert.Empty(infractores);
    }

    [Fact]
    public void Listados_ConGrillaListadoYBusqueda()
    {
        // Listado = grilla con filtros y paginado: todos son GrillaListado (misma configuración,
        // estado que sobrevive a la navegación y pie) con FiltrosGrilla sobre la misma vista.
        var sueltos = Grillas()
            .Where(g => g.Archivo != "GrillaListado.razor"
                        && g.Etiqueta.Contains(@"AllowFiltering=""true""") && g.Etiqueta.Contains(@"AllowPaging=""true"""))
            .Select(g => g.Archivo)
            .ToList();
        Assert.Empty(sueltos);

        var listados = Directory.EnumerateFiles(Componentes(), "*.razor", SearchOption.AllDirectories)
            .Select(f => (Archivo: Path.GetFileName(f), Pagina: File.ReadAllText(f)))
            .Where(p => p.Pagina.Contains("<GrillaListado "))
            .ToList();
        var sinBusqueda = listados
            .Where(p => !Regex.IsMatch(p.Pagina, @"<FiltrosGrilla Vista=""@vista""")
                        || !Regex.IsMatch(p.Pagina, @"<GrillaListado Vista=""@vista"""))
            .Select(p => p.Archivo)
            .ToList();
        Assert.Equal(6, listados.Count);
        Assert.Empty(sinBusqueda);

        // La grilla del componente: Data estable (una lista armada en el render la devolvería a
        // la página 1 con cada redibujo) y los handlers del estado.
        var grilla = Grillas().Single(g => g.Archivo == "GrillaListado.razor").Etiqueta;
        // @key: con otra vista (otra obra sin salir de la página) la grilla se crea de nuevo;
        // @ref solo se asigna al crearla.
        Assert.Contains(@"@key=""Vista""", grilla);
        Assert.Contains(@"Data=""@Vista.Filas""", grilla);
        Assert.Contains(@"SettingsChanged=""@Vista.GuardarSettings""", grilla);
        Assert.Contains(@"LoadSettings=""@Vista.CargarSettings""", grilla);
    }

    [Fact]
    public void Listados_TodasLasColumnasConAncho()
    {
        // Con table-layout fixed una columna sin Width recibe solo el sobrante: en una notebook
        // con zoom, Nombre y Contratista de Obras quedaban en «…». Con todas declaradas la
        // tabla scrollea a lo ancho en vez de truncar, y en pantallas anchas reparte el sobrante.
        var listado = new Regex(@"<GrillaListado\b[^>]*>(?<cuerpo>.*?)</GrillaListado>", RegexOptions.Singleline);
        var columna = new Regex(@"<RadzenDataGridColumn\b(?:[^>""]|""[^""]*"")*>");
        var columnas = Directory.EnumerateFiles(Componentes(), "*.razor", SearchOption.AllDirectories)
            .SelectMany(f => listado.Matches(File.ReadAllText(f))
                .SelectMany(b => columna.Matches(b.Groups["cuerpo"].Value))
                .Select(c => (Archivo: Path.GetFileName(f), Etiqueta: c.Value)))
            .ToList();
        var sinAncho = columnas
            .Where(c => !Regex.IsMatch(c.Etiqueta, @"\sWidth=""\d+px"""))
            .Select(c => $"{c.Archivo}: {Regex.Match(c.Etiqueta, @"\sTitle=""[^""]*""").Value.Trim()}")
            .ToList();

        Assert.NotEmpty(columnas);
        Assert.Empty(sinAncho);
    }

    [Fact]
    public void Tablas_SinFilasCentradasNiSangriaArmadaAMano()
    {
        // Cifras a la derecha (.num) y centrado solo por celda (.centro): una fila centrada
        // entera desencolumnaba los decimales. La sangría de las tablas jerárquicas sale de
        // FilaEstructuraUi.Sangria, en la Descripción: armada a mano se invertía entre rubro e ítem.
        var infractores = Directory.EnumerateFiles(Componentes(), "*.razor", SearchOption.AllDirectories)
            .SelectMany(f =>
            {
                var texto = File.ReadAllText(f);
                var hallados = new List<string>();
                if (Regex.IsMatch(texto, @"<tr\b[^>]*text-align\s*:\s*center")) hallados.Add("fila centrada");
                if (Regex.IsMatch(texto, @"nivel\s*\*\s*16")) hallados.Add("sangría a mano");
                return hallados.Select(h => $"{Path.GetFileName(f)}: {h}");
            })
            .ToList();

        Assert.Empty(infractores);
    }

    [Fact]
    public void Estilos_GrillasConPisoYTitulosAlineadosConSusDatos()
    {
        var css = File.ReadAllText(Path.Combine(Componentes(), "..", "wwwroot", "css", "app-blazor.css"));

        // Sin piso, con zoom los listados mostraban 0 o 1 fila.
        var grilla = Regex.Match(css, @"\.app-content > \.rz-stack > \.rz-data-grid \{(?<reglas>[^}]*)\}");
        Assert.True(grilla.Success, "no se encontró la regla de las grillas del layout");
        Assert.Matches(@"min-height:\s*20rem", grilla.Groups["reglas"].Value);

        // El título de cada columna arranca a la misma distancia del borde que sus datos.
        var celda = Regex.Match(css, @"--rz-grid-cell-padding:\s*[\d.]+rem\s+(?<x>[\d.]+rem)");
        var titulo = Regex.Match(css, @"--rz-grid-header-title-padding-inline:\s*(?<x>[\d.]+rem)");
        Assert.True(celda.Success && titulo.Success, "faltan los tokens de padding de la grilla");
        Assert.Equal(celda.Groups["x"].Value, titulo.Groups["x"].Value);
        Assert.DoesNotMatch(@"\.rz-grid-table td\s*\{[^}]*!important", css);
    }

    [Fact]
    public void GrillasConFiltro_TodasLasColumnasFiltrables()
    {
        // Las calculadas también: muestran una propiedad del VM (CicloDelMes, RolesTexto…), que
        // es lo que ofrece su filtro. Solo la columna de acciones (título vacío o «Acciones») no
        // filtra. Sin FilterProperty: en el filtro de lista, si difiere de Property, Radzen toma
        // Property como colección (SelectMany) y abrir el filtro rompía la página.
        var bloque = new Regex(@"<(?<tag>RadzenDataGrid|GrillaListado)\b(?<apertura>(?:[^>""]|""[^""]*"")*)>(?<cuerpo>.*?)</\k<tag>>",
            RegexOptions.Singleline);
        var columna = new Regex(@"<RadzenDataGridColumn\b(?:[^>""]|""[^""]*"")*>");
        var columnas = Directory.EnumerateFiles(Componentes(), "*.razor", SearchOption.AllDirectories)
            .SelectMany(f => bloque.Matches(File.ReadAllText(f))
                .Where(b => b.Groups["tag"].Value == "GrillaListado" || b.Groups["apertura"].Value.Contains(@"AllowFiltering=""true"""))
                .SelectMany(b => columna.Matches(b.Groups["cuerpo"].Value))
                .Where(c => !Regex.IsMatch(c.Value, @"\sTitle=""(Acciones)?"""))
                .Select(c => (Archivo: Path.GetFileName(f), Etiqueta: c.Value)))
            .ToList();
        var infractores = columnas
            .Where(c => !Regex.IsMatch(c.Etiqueta, @"\sProperty=""")
                        || c.Etiqueta.Contains("Filterable=")
                        || c.Etiqueta.Contains("FilterProperty="))
            .Select(c => $"{c.Archivo}: {Regex.Match(c.Etiqueta, @"\sTitle=""[^""]*""").Value.Trim()}")
            .ToList();

        Assert.NotEmpty(columnas);
        Assert.Empty(infractores);
    }
}
