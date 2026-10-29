using System.Collections;
using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Radzen;
using Radzen.Blazor;

namespace SIGO.Components.Shared;

/// <summary>
/// Textos compartidos de las grillas (portado de SAF): el vacío por filtros estaba copiado
/// en 7 grillas y el popup de filtro mostraba los textos por defecto de Radzen, en inglés
/// («Clear» / «Apply»).
/// </summary>
public static class GridTextos
{
    private static readonly CultureInfo EsAr = CultureInfo.GetCultureInfo("es-AR");

    /// <summary>Vacío por filtros de las grillas sin búsqueda rápida.</summary>
    public const string SinCoincidencias = "Ningún registro coincide con los filtros aplicados.";

    /// <summary>Vacío de los listados con filas: dice cómo volver a ver todo.</summary>
    public const string SinCoincidenciasListado =
        "Ningún registro coincide con la búsqueda o los filtros aplicados. «Quitar filtros», arriba, vuelve a mostrar todo.";

    /// <summary>Vacío de un listado sin filas (Índices muestra la grilla igual).</summary>
    public const string SinRegistros = "No hay registros para mostrar.";

    /// <summary>
    /// Vacío de los listados: sin filas no hay búsqueda ni «Quitar filtros» en la cabecera,
    /// así que el texto que los nombra no corresponde.
    /// </summary>
    public static string VacioListado(int total) => total == 0 ? SinRegistros : SinCoincidenciasListado;

    // Botones del popup de filtro por columna.
    public const string LimpiarFiltro = "Limpiar";
    public const string AplicarFiltro = "Aplicar";

    /// <summary>
    /// Pie de los listados: «N de M filas (vista filtrada)» solo cuando la búsqueda o los
    /// filtros recortan, que es la señal de que lo que se ve no es todo lo que hay.
    /// </summary>
    public static string Filas(int visibles, int total)
    {
        var filas = total == 1 ? "fila" : "filas";
        return visibles == total
            ? $"{total.ToString("N0", EsAr)} {filas}"
            : $"{visibles.ToString("N0", EsAr)} de {total.ToString("N0", EsAr)} {filas} (vista filtrada)";
    }
}

/// <summary>
/// Estado de las grillas de los listados que sobrevive a la navegación (portado de SAF):
/// cambiar de página destruye la grilla, y con ella los filtros, el orden, los anchos, la
/// página y la búsqueda; al volver de una obra había que rearmarlos. Scoped: en Blazor
/// Server vive lo que vive el circuito (la pestaña), y un F5 lo vacía.
/// </summary>
public sealed class EstadoGrillas
{
    private readonly Dictionary<string, EstadoGrilla> porClave = new(StringComparer.Ordinal);

    /// <summary>El estado de la grilla de esa clave (la ruta del listado); vacío la primera vez.</summary>
    public EstadoGrilla De(string clave)
    {
        if (!porClave.TryGetValue(clave, out var estado))
            porClave[clave] = estado = new EstadoGrilla();
        return estado;
    }
}

/// <summary>Lo que un listado guarda entre visitas.</summary>
public sealed class EstadoGrilla
{
    /// <summary>Filtros, orden, anchos y página, tal como los reporta Radzen.</summary>
    public DataGridSettings? Settings { get; set; }

    /// <summary>Texto de la búsqueda rápida.</summary>
    public string Busqueda { get; set; } = string.Empty;
}

/// <summary>
/// Un listado: las filas que pasan la búsqueda rápida y los handlers que guardan y reponen
/// el estado (EstadoGrilla). La comparten la grilla (GrillaListado) y la búsqueda de la
/// cabecera (FiltrosGrilla), que se enteran de los cambios por <see cref="Cambio"/>.
/// </summary>
public sealed class VistaGrilla<TItem> where TItem : notnull
{
    // Invariante y no es-AR: en el orden del español la ñ es otra letra y «albani» no
    // encontraría «Albañilería»; para buscar conviene que sea una n con tilde.
    private static readonly CompareInfo Comparacion = CultureInfo.InvariantCulture.CompareInfo;

    private readonly EstadoGrilla estado;
    private readonly Func<TItem, IEnumerable<string?>> camposBusqueda;
    private IReadOnlyList<TItem> todas = [];

    /// <param name="estado">Estado guardado del listado (<see cref="EstadoGrillas.De"/>).</param>
    /// <param name="camposBusqueda">Campos que identifican la fila, los que recorre la búsqueda rápida.</param>
    public VistaGrilla(EstadoGrilla estado, Func<TItem, IEnumerable<string?>> camposBusqueda)
    {
        this.estado = estado;
        this.camposBusqueda = camposBusqueda;
    }

    /// <summary>La grilla (@ref): quitar filtros y el pie la necesitan.</summary>
    public RadzenDataGrid<TItem>? Grid { get; set; }

    /// <summary>
    /// Cambiaron las filas, la búsqueda o los filtros. La grilla y la búsqueda están en
    /// componentes distintos de la página: cada uno se redibuja con este aviso, sin depender
    /// de que la página se redibuje.
    /// </summary>
    public event Action? Cambio;

    /// <summary>
    /// Data de la grilla. Es otra lista solo cuando cambian los datos o la búsqueda: Radzen
    /// vuelve a la página 1 cada vez que recibe otra Data, así que armarla en cada render
    /// perdía la página con cualquier redibujo.
    /// </summary>
    public IReadOnlyList<TItem> Filas { get; private set; } = [];

    /// <summary>Filas cargadas, sin búsqueda ni filtros.</summary>
    public int Total => todas.Count;

    /// <summary>Filas que quedan a la vista con la búsqueda y los filtros de columna.</summary>
    public int Visibles => Grid?.View.Count() ?? Filas.Count;

    public string Busqueda => estado.Busqueda;

    /// <summary>
    /// Hay algo para quitar: búsqueda o filtros de columna. Mira también lo guardado porque
    /// al volver al listado la cabecera se pinta antes de que la grilla nueva lo reponga.
    /// </summary>
    public bool HayFiltros =>
        !string.IsNullOrWhiteSpace(estado.Busqueda)
        || (Grid?.ColumnsCollection.Any(c => TieneValor(c.GetFilterValue()) || TieneValor(c.GetSecondFilterValue())) ?? false)
        || (estado.Settings?.Columns?.Any(c => TieneValor(c.FilterValue) || TieneValor(c.SecondFilterValue)) ?? false);

    /// <summary>Datos nuevos (carga o recarga tras una baja); se les aplica la búsqueda guardada.</summary>
    public void Cargar(IReadOnlyList<TItem> filas)
    {
        todas = filas;
        Filtrar();
        Cambio?.Invoke();
    }

    public void Buscar(string texto)
    {
        estado.Busqueda = texto;
        Filtrar();
        Cambio?.Invoke();
    }

    /// <summary>Handler de SettingsChanged: Radzen lo llama en cada filtro, orden, ancho o página.</summary>
    public void GuardarSettings(DataGridSettings settings)
    {
        estado.Settings = settings;
        Cambio?.Invoke();
    }

    /// <summary>
    /// Handler de LoadSettings: Radzen lo llama después de cada render, con las columnas ya
    /// registradas, y aplica lo que recibe si es otro objeto que el que ya tiene. Es el
    /// camino para reponer (como en SAF): el parámetro Settings se pierde si un redibujo
    /// llega entre el primer render de la grilla y su OnAfterRender.
    /// </summary>
    public void CargarSettings(DataGridLoadSettingsEventArgs args)
    {
        if (estado.Settings is not { } guardados) return;

        // Una página que con las filas de hoy no existe (se editó o se borró una fila que
        // la llenaba) dejaba la grilla vacía con el pie diciendo que había filas.
        var porPagina = Math.Max(1, guardados.PageSize ?? Grid?.PageSize ?? 20);
        if (guardados.CurrentPage is int pagina && pagina > 0 && pagina * porPagina >= Filas.Count)
            guardados.CurrentPage = Math.Max(0, (Filas.Count - 1) / porPagina);

        if (QuitarFiltrosDeColumnasAusentes(guardados))
            Cambio?.Invoke();

        args.Settings = guardados;
    }

    /// <summary>
    /// Filtros guardados de columnas que la grilla ya no tiene (en Planificación, las que
    /// dependen del corte): no filtran nada, pero mostraban «Quitar filtros» sin motivo y
    /// volvían a aplicarse al volver al listado.
    /// </summary>
    private bool QuitarFiltrosDeColumnasAusentes(DataGridSettings guardados)
    {
        // Sin columnas registradas todavía no se puede saber cuáles faltan.
        if (Grid is null || Grid.ColumnsCollection.Count == 0) return false;

        var quito = false;
        foreach (var columna in guardados.Columns ?? [])
        {
            if (!TieneValor(columna.FilterValue) && !TieneValor(columna.SecondFilterValue)) continue;
            // UniqueID vale "" por defecto (no null): solo identifica si se le puso uno.
            if (Grid.ColumnsCollection.Any(c => c.Property == columna.Property
                    || (!string.IsNullOrEmpty(columna.UniqueID) && c.UniqueID == columna.UniqueID)))
                continue;
            columna.FilterValue = null;
            columna.SecondFilterValue = null;
            quito = true;
        }
        return quito;
    }

    /// <summary>
    /// Radzen 10.4.3 guarda las opciones del filtro de lista de cada columna y solo las
    /// recalcula al filtrar o quitar un filtro: con AllowColumnResize, otra Data no las
    /// invalida, y el popup ofrecía valores de antes de buscar, recargar o cambiar el corte.
    /// Vaciar esa caché es lo único que hace este método interno de la columna; GrillasTests
    /// falla si una actualización de Radzen lo renombra.
    /// </summary>
    private static readonly MethodInfo? VaciarOpciones =
        typeof(RadzenDataGridColumn<TItem>).GetMethod("ClearFilterValues", BindingFlags.Instance | BindingFlags.NonPublic, []);

    /// <summary>Hay cómo refrescar las opciones de los filtros (ver <see cref="RefrescarOpcionesDeFiltro"/>).</summary>
    public static bool PuedeRefrescarOpciones => VaciarOpciones is not null;

    /// <summary>
    /// Las opciones de los filtros de lista se recalculan la próxima vez que se abren. Se
    /// llama sola al cambiar las filas; una página que cambia valores de las filas en el
    /// lugar (el corte de Planificación) la llama a mano.
    /// </summary>
    public void RefrescarOpcionesDeFiltro()
    {
        foreach (var columna in Grid?.ColumnsCollection ?? [])
            VaciarOpciones?.Invoke(columna, null);
    }

    /// <summary>Quita la búsqueda y los filtros de columna; conserva orden, anchos y página.</summary>
    public async Task QuitarFiltrosAsync()
    {
        foreach (var columna in estado.Settings?.Columns ?? [])
        {
            columna.FilterValue = null;
            columna.SecondFilterValue = null;
        }
        if (Grid is not null)
        {
            foreach (var columna in Grid.ColumnsCollection)
                columna.ClearFilters();
            await Grid.Reload();
            // ClearFilters no avisa a SettingsChanged: sin esto, al volver al listado
            // se repondrían los filtros recién quitados.
            Grid.SaveSettings();
        }
        // Después de guardar: si cambia la Data, la página guardada vuelve a la 1 (Filtrar).
        if (estado.Busqueda.Length > 0)
            Buscar(string.Empty);
        else
            Cambio?.Invoke();
    }

    private void Filtrar()
    {
        var texto = estado.Busqueda.Trim();
        Filas = texto.Length == 0
            ? todas
            : todas.Where(fila => camposBusqueda(fila).Any(valor => Contiene(valor, texto))).ToList();

        // Con la grilla viva, la Data nueva la lleva a la página 1 sin pasar por
        // SettingsChanged: lo guardado también, para no reponer al volver una página que
        // con menos filas puede no existir.
        if (Grid is not null && estado.Settings is { } guardados)
            guardados.CurrentPage = 0;
        RefrescarOpcionesDeFiltro();
    }

    // Sin mayúsculas ni tildes: «indice» encuentra «Índice» y «albani», «Albañilería».
    private static bool Contiene(string? valor, string texto) =>
        valor is not null
        && Comparacion.IndexOf(valor, texto, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;

    // El filtro de lista (CheckBoxList) sin nada tildado deja una colección vacía, no null.
    private static bool TieneValor(object? valor) => valor switch
    {
        null => false,
        string s => s.Length > 0,
        IEnumerable lista => lista.Cast<object?>().Any(),
        _ => true
    };
}

/// <summary>
/// Base de los componentes de un listado (GrillaListado, FiltrosGrilla): se redibujan
/// cuando la <see cref="VistaGrilla{TItem}"/> avisa un cambio.
/// </summary>
public abstract class ComponenteDeVista<TItem> : ComponentBase, IDisposable where TItem : notnull
{
    private VistaGrilla<TItem>? suscripta;

    [Parameter, EditorRequired] public VistaGrilla<TItem> Vista { get; set; } = null!;

    protected override void OnParametersSet()
    {
        // La página arma otra vista si cambia la obra o el índice del listado.
        if (ReferenceEquals(suscripta, Vista)) return;
        if (suscripta is not null) suscripta.Cambio -= Redibujar;
        suscripta = Vista;
        suscripta.Cambio += Redibujar;
    }

    private void Redibujar() => _ = InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        if (suscripta is not null) suscripta.Cambio -= Redibujar;
    }
}
