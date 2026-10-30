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
            .Where(c => !Regex.IsMatch(c.Etiqueta, @"\sWidth=""(\d+px|@AnchoColumna\.\w+(\(\d+\))?)"""))
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

    private static IEnumerable<(string Archivo, string Texto)> Paginas() =>
        Directory.EnumerateFiles(Componentes(), "*.razor", SearchOption.AllDirectories)
            .Select(f => (Path.GetFileName(f), File.ReadAllText(f)));

    // Etiqueta de apertura completa, con valores entre comillas que pueden tener «>» o comillas
    // internas de una interpolación (@($"…")).
    private static readonly Regex Etiqueta = new(@"<(?<tag>[A-Za-z][\w.]*)\b(?:[^>""]|""[^""]*"")*>");

    // Páginas que no usan CabeceraPagina: la portada y la pantalla de acceso denegado, centradas.
    private static readonly string[] SinCabecera = ["Home.razor", "SinAcceso.razor"];

    [Fact]
    public void Escala_TitulosDePaginaConCabeceraYH4SoloEnLaRaizDelMenu()
    {
        // H4 en las entradas del menú; el resto H5 (el default de CabeceraPagina): el título
        // saltaba de tamaño entre páginas hermanas (Obras H4 → Obra H4 → Certificados H5).
        string[] raiz = ["ObrasList.razor", "IndicesList.razor", "Calcular.razor",
                         "RedeterminacionHistorial.razor", "PlanificacionList.razor", "UsuariosList.razor"];
        var conH4 = Paginas().Where(p => p.Texto.Contains(@"Estilo=""TextStyle.H4""")).Select(p => p.Archivo).ToList();
        Assert.Equal(raiz.Order(), conH4.Order());
        // Ningún otro tamaño explícito: H5 es el default.
        Assert.Empty(Paginas().Where(p => Regex.IsMatch(p.Texto, @"Estilo=""TextStyle\.(?!H4"")")).Select(p => p.Archivo));

        // Toda página con ruta usa CabeceraPagina, que pone el único H1.
        var sinCabecera = Paginas()
            .Where(p => Regex.IsMatch(p.Texto, @"^@page\b", RegexOptions.Multiline)
                        && !p.Texto.Contains("<CabeceraPagina") && !SinCabecera.Contains(p.Archivo))
            .Select(p => p.Archivo);
        Assert.Empty(sinCabecera);
        var h1Propios = Paginas()
            .Where(p => p.Archivo is not ("CabeceraPagina.razor" or "Home.razor")
                        && Regex.IsMatch(p.Texto, @"TagName\.H1|<h1\b"))
            .Select(p => p.Archivo);
        Assert.Empty(h1Propios);

        // Títulos de tarjeta: H6 con TagName.H2 (sin él quedaban <h6> colgando del <h1>).
        var h6SinNivel = Paginas()
            .SelectMany(p => Etiqueta.Matches(p.Texto)
                .Where(m => m.Groups["tag"].Value == "RadzenText" && m.Value.Contains("TextStyle.H6")
                            && !m.Value.Contains("TagName.H2"))
                .Select(_ => p.Archivo));
        Assert.Empty(h6SinNivel);
    }

    [Fact]
    public void Escala_UnaBarraUnTamano_BotonesSmallEnLasAcciones()
    {
        // Barras de acciones (cabecera de página, banner de resultado): botones Small. Medium
        // queda para el envío de formularios y diálogos; ExtraSmall, dentro de celdas.
        var acciones = new Regex(@"<Acciones>(?<cuerpo>.*?)</Acciones>", RegexOptions.Singleline);
        var boton = new Regex(@"<RadzenButton\b(?:[^>""]|""[^""]*"")*>");
        var infractores = Paginas()
            .SelectMany(p => acciones.Matches(p.Texto)
                .SelectMany(a => boton.Matches(a.Groups["cuerpo"].Value))
                .Where(b => !b.Value.Contains(@"Size=""ButtonSize.Small"""))
                .Select(b => $"{p.Archivo}: {Regex.Match(b.Value, @"Text=""[^""]*""").Value}"))
            .ToList();
        Assert.Empty(infractores);

        Assert.Contains(@"Size=""ButtonSize.Small""",
            File.ReadAllText(Path.Combine(Componentes(), "Shared", "FiltrosGrilla.razor")));
    }

    // Múltiplo de 4px: rem en cuartos (0.25rem = 4px) o px múltiplos de 4. 1-2px no son
    // separaciones (bordes, el margin:-1px de .sr-only).
    private static bool EnLaGrillaDe4px(string valor) =>
        Regex.Matches(valor, @"(?<n>-?\d*\.?\d+)(?<u>rem|px)").All(m =>
        {
            var n = decimal.Parse(m.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture);
            return m.Groups["u"].Value == "rem" ? n * 4 % 1 == 0 : Math.Abs(n) <= 2 || n % 4 == 0;
        });

    [Fact]
    public void Escala_SeparacionesEnMultiplosDe4px()
    {
        string[] escala = ["0", "0.25rem", "0.5rem", "0.75rem", "1rem", "1.5rem"];
        var infractores = Paginas()
            .SelectMany(p => Regex.Matches(p.Texto, @"\b(?:Row)?Gap=""(?<v>[^""]*)""")
                .Where(m => !escala.Contains(m.Groups["v"].Value))
                .Select(m => $"{p.Archivo}: {m.Value}")
                // Separaciones inline (style="padding-left:1.1rem").
                .Concat(Regex.Matches(p.Texto, @"(?i)\b(?:margin|padding|gap)[\w-]*\s*:\s*(?<v>[^;""]+)")
                    .Where(m => !EnLaGrillaDe4px(m.Groups["v"].Value))
                    .Select(m => $"{p.Archivo}: {m.Value}")))
            .ToList();

        // Y en el CSS propio. Excepción: los paddings de la sidebar en modo íconos (0.8125rem),
        // calculados para que cada ítem mida lo mismo que el botón de menú de la cabecera.
        var css = File.ReadAllText(Path.Combine(Componentes(), "..", "wwwroot", "css", "app-blazor.css"));
        infractores.AddRange(Regex.Matches(css, @"(?m)^\s*(?:--rz-[\w-]*(?:margin|padding|gap|offset)[\w-]*|margin[\w-]*|padding[\w-]*|gap)\s*:\s*(?<v>[^;]+);")
            .Where(m => !m.Value.Contains("--rz-panel-menu-") && !EnLaGrillaDe4px(m.Groups["v"].Value))
            .Select(m => $"app-blazor.css: {m.Value.Trim()}"));

        Assert.Empty(infractores);
    }

    [Fact]
    public void Escala_SinFuentesNiAnchosSueltos()
    {
        var infractores = Paginas().SelectMany(p =>
        {
            var hallados = new List<string>();
            foreach (Match tag in Etiqueta.Matches(p.Texto))
            {
                // Tamaños de la escala (app-fs-body2 / app-fs-caption); los íconos grandes de los
                // estados vacíos llevan el suyo. «style» o «Style» (el parámetro de Radzen).
                if (tag.Groups["tag"].Value != "RadzenIcon" && Regex.IsMatch(tag.Value, @"(?is)\bstyle\s*=\s*"".*?font-size"))
                    hallados.Add($"font-size inline en {tag.Groups["tag"].Value}");
                // Títulos con app-titulo-bloque y totales con app-total, no con font-weight inline.
                if (tag.Groups["tag"].Value == "RadzenText" && Regex.IsMatch(tag.Value, @"(?is)\bstyle\s*=\s*"".*?font-weight"))
                    hallados.Add("font-weight en RadzenText");
            }
            // Anchos de formulario con app-form-angosto/medio/ancho.
            if (Regex.IsMatch(p.Texto, @"(?i)max-width\s*:\s*[\d.]+\s*(px|rem|em|ch|%|vw)")) hallados.Add("max-width inline");
            // Todo diálogo con AnchoDialogo (sin Width, Radzen usa 600px).
            hallados.AddRange(Regex.Matches(p.Texto, @"new DialogOptions\s*\{[^}]*\}")
                .Where(m => !m.Value.Contains("AnchoDialogo.")).Select(_ => "diálogo sin AnchoDialogo"));
            return hallados.Select(h => $"{p.Archivo}: {h}");
        }).ToList();
        Assert.Empty(infractores);
    }

    [Fact]
    public void Escala_ColumnasDeTiposEstandarConSuAnchoColumna()
    {
        // Había 26 anchos distintos para los mismos datos (una fecha en 110, 120, 130 o 160px). Cada
        // título estándar tiene que usar SU constante (una «Valor» con Fecha también rompía). Las
        // columnas congeladas (el Período de la planificación, con botones) declaran el suyo.
        // N° depende de la grilla: si ordena o filtra, sus íconos no entran en 60px (no se veían).
        string[] numeros = ["N°", "Fila", "#", "Línea"];
        var esperado = new Dictionary<string, string[]>
        {
            ["Acta Inicio"] = ["Fecha"], ["Fin Contrato"] = ["Fecha"], ["Fecha del acto"] = ["Fecha"],
            ["Fin anterior"] = ["Fecha"], ["Nuevo fin"] = ["Fecha"], ["Emisión"] = ["Fecha"],
            ["Período"] = ["Periodo"], ["Código"] = ["Codigo", "CodigoIndice"], ["Unidad"] = ["Unidad"],
            ["Cantidad"] = ["Cantidad"], ["Valor"] = ["Cantidad"], ["Monto"] = ["Importe"], ["PU Básico"] = ["Importe"],
            ["% Pond."] = ["Porcentaje"], ["% mes"] = ["Porcentaje"], ["Peso %"] = ["Porcentaje"],
            ["Estado del plan"] = ["Estado"],
        };
        var bloque = new Regex(@"<(?<tag>RadzenDataGrid|GrillaListado)\b(?<apertura>(?:[^>""]|""[^""]*"")*)>(?<cuerpo>.*?)</\k<tag>>",
            RegexOptions.Singleline);
        var infractores = Paginas()
            .SelectMany(p => bloque.Matches(p.Texto).SelectMany(b =>
            {
                var ordena = b.Groups["tag"].Value == "GrillaListado"
                             || Regex.IsMatch(b.Groups["apertura"].Value, @"Allow(Sorting|Filtering)=""true""");
                return Etiqueta.Matches(b.Groups["cuerpo"].Value)
                    .Where(m => m.Groups["tag"].Value == "RadzenDataGridColumn" && !m.Value.Contains(@"Frozen=""true"""))
                    .Select(m => (Titulo: Regex.Match(m.Value, @"\sTitle=""(?<t>[^""]*)""").Groups["t"].Value, m.Value))
                    .Select(c => (c.Titulo, c.Value,
                        Constantes: numeros.Contains(c.Titulo) ? [ordena ? "NumeroOrdenable" : "Numero"]
                                  : esperado.GetValueOrDefault(c.Titulo)))
                    .Where(c => c.Constantes is not null
                                && !c.Constantes.Any(k => Regex.IsMatch(c.Value, $@"Width=""@AnchoColumna\.{k}""")))
                    .Select(c => $"{p.Archivo}: {c.Titulo}");
            }))
            .ToList();
        Assert.Empty(infractores);
    }

    [Fact]
    public void Reconexion_AvisoPropioEnCastellanoParaCadaEstado()
    {
        // Sin el elemento, Blazor muestra su aviso en inglés. Con él, solo le pone una clase
        // por estado (blazor.web.js de .NET 10): cada estado necesita su texto y su estilo.
        var app = File.ReadAllText(Path.Combine(Componentes(), "App.razor"));
        var css = File.ReadAllText(Path.Combine(Componentes(), "..", "wwwroot", "css", "app-blazor.css"));

        Assert.Contains(@"id=""components-reconnect-modal""", app);
        foreach (var bloque in new[] { "app-reconexion-intentando", "app-reconexion-pausada", "app-reconexion-fallida", "app-reconexion-rechazada" })
            Assert.Contains(bloque, app);
        foreach (var estado in new[] { "show", "retrying", "paused", "failed", "resume-failed", "rejected" })
            Assert.Contains($".components-reconnect-{estado} .app-reconexion-", css);
        // Con un aviso propio Blazor no recarga solo: la sesión rechazada necesita el botón.
        Assert.Contains("location.reload()", app);
    }

    [Fact]
    public void Celdas_EnTextoSinBadgesNiColorDeEstado()
    {
        // Un badge por fila (fondo y color en cada renglón) era ruido: el estado, el rol, el tipo o
        // un conteo se escriben. El color de alerta (app-texto-error/aviso) queda para lo que hay
        // que corregir en una vista previa o una carga; en los listados, ni eso.
        var grilla = new Regex(@"<(?<tag>RadzenDataGrid|GrillaListado|table)\b(?:[^>""]|""[^""]*"")*>(?<cuerpo>.*?)</\k<tag>>",
            RegexOptions.Singleline);
        var infractores = Paginas()
            .SelectMany(p => grilla.Matches(p.Texto).SelectMany(b =>
            {
                var cuerpo = b.Groups["cuerpo"].Value;
                var hallados = new List<string>();
                if (cuerpo.Contains("<RadzenBadge")) hallados.Add("badge en una celda");
                if (cuerpo.Contains("IconColor=")) hallados.Add("ícono en color en una celda");
                if (b.Groups["tag"].Value == "GrillaListado" && Regex.IsMatch(cuerpo, @"color\s*:|app-texto-"))
                    hallados.Add("color en un listado");
                return hallados.Select(h => $"{p.Archivo}: {h}");
            }))
            .ToList();

        Assert.Empty(infractores);
    }

    [Fact]
    public void Titulos_SinContadores()
    {
        // Un badge con la cantidad junto a un título («Prórrogas de plazo 0») era ruido: la lista
        // está a la vista y, vacía, lo dice su texto («Sin prórrogas…»).
        var contador = new Regex(@"<RadzenBadge\b[^>]*\sText=""@\(?[\w.]*(Count|Cantidad)\w*(\(\))?\.ToString\(\)\)?""");
        var infractores = Paginas()
            .SelectMany(p => contador.Matches(p.Texto).Select(m => $"{p.Archivo}: {m.Value}"))
            .ToList();

        Assert.Empty(infractores);
    }

    [Fact]
    public void SeccionesDeLaObra_VuelvenADondeSeEntro()
    {
        // Entrando desde las columnas de acceso del listado, «Volver a Obra» llevaba a la ficha y
        // no al listado. Las columnas marcan RetornoObra, la ficha lo desmarca y las secciones
        // leen de ahí destino y texto. Los tres accesos usan el mismo ícono de ir (la columna dice
        // a qué sección), Primary como Editar.
        var p = Path.Combine(Componentes(), "Pages");
        var listado = File.ReadAllText(Path.Combine(p, "Obras", "ObrasList.razor"));
        var ficha = File.ReadAllText(Path.Combine(p, "Obras", "ObraDetails.razor"));

        Assert.Contains("Retorno.DesdeListado(obra.Id)", listado);
        Assert.DoesNotMatch(@"NavigateTo\(\$""obras/\{obra\.Id\}/(estructuras|certificados|ponderacion)""", listado);
        Assert.Equal(3, Regex.Matches(listado, @"<RadzenButton Icon=""arrow_forward""[^>]*ButtonStyle=""ButtonStyle\.Primary""").Count);
        Assert.Contains("Retorno.DesdeFicha()", ficha);
        foreach (var seccion in new[] { Path.Combine("Estructuras", "EstructurasList.razor"),
                                        Path.Combine("Certificados", "CertificadosList.razor"),
                                        Path.Combine("Redeterminacion", "PonderacionEditar.razor") })
        {
            var texto = File.ReadAllText(Path.Combine(p, seccion));
            Assert.Contains(@"Text=""@Retorno.Volver(ObraId).Texto""", texto);
            Assert.Contains("Nav.NavigateTo(Retorno.Volver(ObraId).Ruta)", texto);
            Assert.DoesNotContain(@"Text=""Volver a Obra""", texto);
        }
    }

    [Fact]
    public void Enlaces_SinAnclaSuelta()
    {
        // Con <base href="/">, href="#x" se resuelve contra la base («/#x»): desde cualquier página
        // que no fuera la portada, «Saltar al contenido» navegaba al inicio. El skip-link apunta a
        // la página actual con el ancla y mueve el foco al contenido.
        var sueltas = Paginas().Where(p => p.Texto.Contains(@"href=""#")).Select(p => p.Archivo).ToList();
        var layout = File.ReadAllText(Path.Combine(Componentes(), "Layout", "MainLayout.razor"));

        Assert.Empty(sueltas);
        Assert.Contains(@"href=""@HrefContenido""", layout);
        Assert.Contains("contenido.FocusAsync()", layout);
    }

    [Fact]
    public void Estilos_SinColoresFijosPorNombre()
    {
        // Los colores salen de tokens --rz-* para que valgan en claro y en oscuro: el «gray» fijo
        // del borde de la cabecera de las grillas no seguía al tema.
        var css = Regex.Replace(File.ReadAllText(Path.Combine(Componentes(), "..", "wwwroot", "css", "app-blazor.css")),
            @"/\*.*?\*/", "", RegexOptions.Singleline);
        var fijos = Regex.Matches(css, @"(?<![\w-])(gray|grey|silver|black|white)(?![\w-])", RegexOptions.IgnoreCase)
            .Select(m => m.Value)
            .ToList();

        Assert.Empty(fijos);
    }

    [Fact]
    public void Foco_AlNavegarEnElTituloSinRecuadro()
    {
        // FocusOnNavigate enfoca el H1 para que el lector de pantalla anuncie la página; sin la
        // regla del CSS, el navegador lo marcaba con el recuadro del foco al cargar o recargar.
        var rutas = File.ReadAllText(Path.Combine(Componentes(), "Routes.razor"));
        var css = File.ReadAllText(Path.Combine(Componentes(), "..", "wwwroot", "css", "app-blazor.css"));

        Assert.Contains(@"<FocusOnNavigate RouteData=""@routeData"" Selector=""h1"" />", rutas);
        Assert.Matches(@"(?m)^h1:focus\s*\{\s*outline:\s*none;", css);
    }

    [Fact]
    public void GrillasConFiltro_TodasLasColumnasFiltrables()
    {
        // Las calculadas también: muestran una propiedad del VM (CicloDelMes, RolesTexto…), que
        // es lo que ofrece su filtro. Solo no filtran las columnas de botones, que no muestran un
        // dato: la de acciones (título vacío o «Acciones») y las de acceso a otra página (sin
        // Property, con un botón: Estructuras, Certificados, Ponderación en Obras). Sin
        // FilterProperty: en el filtro de lista, si difiere de Property, Radzen toma Property como
        // colección (SelectMany) y abrir el filtro rompía la página.
        var bloque = new Regex(@"<(?<tag>RadzenDataGrid|GrillaListado)\b(?<apertura>(?:[^>""]|""[^""]*"")*)>(?<cuerpo>.*?)</\k<tag>>",
            RegexOptions.Singleline);
        // La columna entera: la etiqueta de apertura y, si no se cierra sola, su contenido.
        var columna = new Regex(@"<RadzenDataGridColumn\b(?:[^>""/]|""[^""]*""|/(?!>))*(?:/>|>(?<cuerpo>.*?)</RadzenDataGridColumn>)",
            RegexOptions.Singleline);
        var columnas = Directory.EnumerateFiles(Componentes(), "*.razor", SearchOption.AllDirectories)
            .SelectMany(f => bloque.Matches(File.ReadAllText(f))
                .Where(b => b.Groups["tag"].Value == "GrillaListado" || b.Groups["apertura"].Value.Contains(@"AllowFiltering=""true"""))
                .SelectMany(b => columna.Matches(b.Groups["cuerpo"].Value))
                .Select(c => (Etiqueta: Etiqueta.Match(c.Value).Value, Cuerpo: c.Groups["cuerpo"].Value))
                .Where(c => !Regex.IsMatch(c.Etiqueta, @"\sTitle=""(Acciones)?""")
                            && !(c.Cuerpo.Contains("<RadzenButton") && !Regex.IsMatch(c.Etiqueta, @"\sProperty=""")))
                .Select(c => (Archivo: Path.GetFileName(f), c.Etiqueta)))
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
