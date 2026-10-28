using ClosedXML.Excel;
using SIGO.Models;
using SIGO.Models.Enums;
using SIGO.Services.Validaciones;

namespace SIGO.Services.Importacion;

/// <summary>Layouts de planilla que se reconocen para cargar ítems de estructura.</summary>
public enum LayoutEstructura
{
    /// <summary>«Planilla de Desglose» del Anexo VII: Item, Descripción, U, Cantidad, Precio Unitario, Precio Subtotal, TOTAL.</summary>
    Desglose,
    /// <summary>«Planilla de Cotización»: Rubro, Descripción, U, Cantidad, Subtotal (sin precio unitario, se deriva).</summary>
    Cotizacion,
    /// <summary>«Balance» de un BED: contrato + grupos de columnas ECONOMIA / DEMASIA / BED, cada uno con Cantidad, Valor Unitario y Subtotal.</summary>
    Balance
}

/// <summary>Grupo de columnas del layout Balance que se toma como cantidad y precio del ítem.</summary>
public enum GrupoBalance { Demasia, Economia, Bed, Contrato }

public enum TipoFilaEstructura
{
    Rubro,
    SubRubro,
    Item,
    /// <summary>Ítem sin cantidad o sin precio (los «No aplica»), o con alguno en cero: se importa sin monto o con monto 0.</summary>
    ItemSinMonto,
    /// <summary>Ítem con unidad o números pero sin código: se importa y se marca.</summary>
    ItemSinCodigo,
    /// <summary>Layout Balance: ítem con cantidad solo en Economía. No se importa (las economías van como % negativo en el básico).</summary>
    Economia,
    Ignorada
}

/// <summary>Una fila analizada de la planilla, con su clasificación y su lugar en el árbol.</summary>
public sealed class FilaEstructuraImport
{
    public int Fila { get; init; }
    public TipoFilaEstructura Tipo { get; set; }
    public int Nivel { get; set; }
    /// <summary>Índice del agrupador padre dentro de la lista de la sección (null = raíz).</summary>
    public int? PadreIndice { get; set; }
    public string? Codigo { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public string? Unidad { get; set; }
    public decimal? Cantidad { get; set; }
    public decimal? PUBasico { get; set; }
    /// <summary>Monto según la regla de la app (Cantidad × PU a 4 decimales), no el subtotal de la planilla.</summary>
    public decimal? Monto { get; set; }
    public TipoMovimiento? TipoMovimiento { get; set; }
    public string? Detalle { get; set; }

    public bool EsAgrupador => Tipo is TipoFilaEstructura.Rubro or TipoFilaEstructura.SubRubro;
    public bool SeImporta => Tipo is TipoFilaEstructura.Rubro or TipoFilaEstructura.SubRubro
        or TipoFilaEstructura.Item or TipoFilaEstructura.ItemSinMonto or TipoFilaEstructura.ItemSinCodigo;

    internal void AgregarDetalle(string texto) =>
        Detalle = Detalle is null ? texto : $"{Detalle}; {texto}";

    /// <summary>
    /// Copia con el índice del padre corrido (para unir varias secciones en una lista).
    /// MemberwiseClone copia todas las propiedades: una que se agregue no se pierde al combinar.
    /// </summary>
    internal FilaEstructuraImport ConPadreDesplazado(int desplazamiento)
    {
        var copia = (FilaEstructuraImport)MemberwiseClone();
        copia.PadreIndice = PadreIndice + desplazamiento;
        return copia;
    }
}

/// <summary>Tramo de la hoja entre dos filas de TOTAL (una planilla de cotización trae varios).</summary>
public sealed class SeccionEstructuraImport
{
    public int Indice { get; init; }
    public string Titulo { get; set; } = string.Empty;
    public int FilaDesde { get; init; }
    public int FilaHasta { get; set; }
    public List<FilaEstructuraImport> Filas { get; } = [];
    /// <summary>Total de la fila «TOTAL …» que cierra la sección, para contrastar con el calculado (null si termina sin total).</summary>
    public decimal? TotalPlanilla { get; set; }

    /// <summary>Texto de la lista de secciones: «01 TRABAJOS PRELIMINARES (filas 9–28)».</summary>
    public string Etiqueta => $"{Titulo} (filas {FilaDesde}–{FilaHasta})";

    public IEnumerable<FilaEstructuraImport> ItemsAImportar => Filas.Where(f => f.SeImporta && !f.EsAgrupador);
    public int CantidadItems => ItemsAImportar.Count();
    public int Agrupadores => Filas.Count(f => f.SeImporta && f.EsAgrupador);
    public int Advertencias => Filas.Count(f => f.SeImporta && f.Detalle is not null);
    public int NoImportadas => Filas.Count(f => !f.SeImporta);
    public decimal TotalCalculado => ItemsAImportar.Sum(f => f.Monto ?? 0);
}

public sealed class AnalisisEstructura
{
    public LayoutEstructura Layout { get; init; }
    public List<SeccionEstructuraImport> Secciones { get; } = [];
    /// <summary>Código y nombre del bloque leídos arriba del encabezado (ej. «PEV-SD-EAG-01 / LÍNEA D - ESTACIÓN AGÜERO»).</summary>
    public string? CodigoBloque { get; init; }
    public string? NombreBloque { get; init; }
    /// <summary>Columnas que se usaron, en letras de Excel, para que el usuario verifique la lectura.</summary>
    public string ColumnasDetectadas { get; init; } = string.Empty;
    public string? Error { get; init; }
    /// <summary>El encabezado se reconoció pero debajo no hay ningún ítem (resúmenes, hojas con la fila de títulos copiada).</summary>
    public bool SinItems { get; init; }
}

/// <summary>
/// Una hoja del libro evaluada para importar ítems: el layout reconocido (null si no tiene
/// encabezado de planilla), el texto del desplegable y <c>ConItems</c>: el análisis no la
/// encontró vacía (una hoja con encabezado y nada debajo va al final de la preselección).
/// <c>Falla</c> es la excepción de una hoja que no se pudo evaluar, para que la página la
/// registre.
/// </summary>
public sealed record HojaEstructuraOpcion(HojaExcel Hoja, LayoutEstructura? Layout, string Etiqueta, Exception? Falla = null, bool ConItems = false)
{
    /// <summary>Valor del desplegable: Excel no admite dos hojas con el mismo nombre.</summary>
    public string Nombre => Hoja.Nombre;
}

/// <summary>Resultado de <see cref="ImportadorEstructura.EvaluarHojas"/>: todas las hojas y la preseleccionada (null si ninguna se reconoce).</summary>
public sealed record EleccionHojaEstructura(IReadOnlyList<HojaEstructuraOpcion> Opciones, HojaEstructuraOpcion? Elegida);

/// <summary>
/// Parseo puro (sin base de datos) de una hoja de planilla de desglose, cotización o
/// balance a filas de estructura de costos clasificadas. Las reglas salen de las
/// planillas reales de tres contratistas (ver docs/importacion-excel.md):
/// encabezado detectado por contenido, rubros por prefijo de código o por celda TOTAL,
/// sub-rubros sin código, ítems «No aplica» sin monto, y separadores ignorados.
/// </summary>
public static class ImportadorEstructura
{
    /// <summary>Lo que se busca como encabezado, para los mensajes de la interfaz.</summary>
    public const string EncabezadoBuscado = "una fila con «Item» o «Rubro» y «Descripción»";

    private static readonly string[] Separadores =
        ["items nuevos", "item nuevos", "demasias", "economias", "adicionales", "balance"];

    private sealed record Columnas(
        int Item, int Descripcion, int? Unidad, int? Cantidad, int? PU, int? Subtotal, int? Total,
        int? CantidadContrato, int? PUContrato, int? CantidadEconomia);

    /// <summary>Encabezado detectado. Con <c>Error</c>, el layout se reconoció pero no se puede usar (grupo del balance ausente).</summary>
    private sealed record Encabezado(Columnas? Columnas, LayoutEstructura Layout, int UltimaFilaEncabezado, string? Error = null);

    /// <summary>Layout reconocido en la hoja, o null si no tiene un encabezado de planilla.</summary>
    public static LayoutEstructura? DetectarLayout(HojaExcel hoja) =>
        DetectarEncabezado(hoja, GrupoBalance.Demasia)?.Layout;

    /// <summary>
    /// Evalúa las hojas del libro y elige cuál preseleccionar. Primero las que tienen ítems
    /// (un encabezado sin ítems debajo, como el «RESUMEN $» de Premetro, se marca «sin
    /// ítems» y solo se preselecciona si no hay otra); entre ellas, la primera visible con
    /// desglose o balance; si no hay, la primera visible con cotización. Las ocultas
    /// (copias viejas, auxiliares como « Balance EDyA» del BED de Agüero) después de las
    /// visibles. Una hoja que no se puede evaluar queda en la lista como «no se pudo
    /// analizar» y no impide usar las demás.
    /// </summary>
    public static EleccionHojaEstructura EvaluarHojas(LibroExcel libro)
    {
        var opciones = libro.Hojas.Select(h =>
        {
            LayoutEstructura? layout;
            try
            {
                layout = DetectarLayout(h);
            }
            catch (Exception ex)
            {
                return new HojaEstructuraOpcion(h, null, TextoImport.EtiquetaHoja(h, "no se pudo analizar"), ex);
            }
            if (layout is not LayoutEstructura l)
                return new HojaEstructuraOpcion(h, null, TextoImport.EtiquetaHoja(h, null));

            // El análisis completo (con el grupo por defecto del balance) dice si la hoja
            // está vacía; es lo que después hace la página con la hoja elegida. Un balance
            // al que le falta ese grupo da error pero no está vacío: se usa eligiendo otro
            // grupo, así que conserva su lugar. Si el análisis falla, la hoja conserva el
            // layout: sigue siendo elegible y la página muestra el error al analizarla.
            try
            {
                var analisis = Analizar(h);
                return new HojaEstructuraOpcion(h, l,
                    TextoImport.EtiquetaHoja(h, analisis.SinItems ? $"{NombreLayout(l)}, sin ítems" : NombreLayout(l)),
                    ConItems: !analisis.SinItems);
            }
            catch (Exception ex)
            {
                return new HojaEstructuraOpcion(h, l, TextoImport.EtiquetaHoja(h, $"{NombreLayout(l)}, no se pudo analizar"), ex);
            }
        }).ToList();

        // OrderBy es estable: dentro de cada grupo queda el orden del libro.
        var elegida = opciones
            .Where(o => o.Layout is not null)
            .OrderBy(o => o.ConItems ? 0 : 1)
            .ThenBy(o => o.Hoja.Oculta)
            .ThenBy(o => o.Layout is LayoutEstructura.Desglose or LayoutEstructura.Balance ? 0 : 1)
            .FirstOrDefault();
        return new EleccionHojaEstructura(opciones, elegida);
    }

    /// <summary>
    /// Une las filas de las secciones elegidas en una sola lista, con los índices de
    /// padre corridos: es lo que recibe EstructuraService.ImportarItemsAsync.
    /// </summary>
    public static List<FilaEstructuraImport> Combinar(IEnumerable<SeccionEstructuraImport> secciones)
    {
        var resultado = new List<FilaEstructuraImport>();
        foreach (var s in secciones)
        {
            var desplazamiento = resultado.Count;
            foreach (var f in s.Filas)
                resultado.Add(f.ConPadreDesplazado(desplazamiento));
        }
        return resultado;
    }

    /// <summary>Nombre del layout para la interfaz.</summary>
    public static string NombreLayout(LayoutEstructura layout) => layout switch
    {
        LayoutEstructura.Desglose => "Planilla de desglose",
        LayoutEstructura.Cotizacion => "Planilla de cotización",
        LayoutEstructura.Balance => "Balance de BED",
        _ => layout.ToString()
    };

    /// <summary>
    /// Analiza la hoja: encabezado, secciones cortadas por las filas TOTAL y cada fila de
    /// datos clasificada con su lugar en el árbol (<see cref="AnalizarFila"/>).
    /// <paramref name="grupo"/> solo cuenta en el balance.
    /// </summary>
    public static AnalisisEstructura Analizar(HojaExcel hoja, GrupoBalance grupo = GrupoBalance.Demasia)
    {
        var enc = DetectarEncabezado(hoja, grupo);
        if (enc is null)
            return new AnalisisEstructura { Error = $"No se encontró el encabezado de la planilla ({EncabezadoBuscado})." };
        if (enc.Error is not null)
            return new AnalisisEstructura { Layout = enc.Layout, Error = enc.Error };

        var col = enc.Columnas!;
        var ctx = new Contexto(col, enc.Layout, grupo,
            ContratoNumeros: enc.Layout == LayoutEstructura.Balance && grupo != GrupoBalance.Contrato);
        var (codigoBloque, nombreBloque) = LeerBloque(hoja, enc.UltimaFilaEncabezado, col);
        var analisis = new AnalisisEstructura
        {
            Layout = enc.Layout,
            CodigoBloque = codigoBloque,
            NombreBloque = nombreBloque,
            ColumnasDetectadas = DescribirColumnas(col, enc.Layout)
        };

        var datos = hoja.Filas.Where(f => f.Numero > enc.UltimaFilaEncabezado).ToList();
        var codigos = datos.Select(f => LeerCodigo(f, col)).ToList();
        var siguienteCodigo = SiguientesCodigos(codigos);

        SeccionEstructuraImport? seccion = null;
        var arbol = new ArbolDeSeccion();
        for (var i = 0; i < datos.Count; i++)
        {
            var fila = datos[i];
            var codigo = codigos[i];

            // La fila TOTAL cierra la sección abierta y aporta su total para contrastarlo.
            if (EsFilaTotal(fila, col))
            {
                if (seccion is not null)
                {
                    seccion.TotalPlanilla = LeerTotal(fila, col);
                    CerrarSeccion(analisis, seccion, fila.Numero, ctx.ContratoNumeros);
                    seccion = null;
                }
                continue;
            }

            var (descripcion, textoItem) = LeerDescripcion(fila, col, codigo);
            if (codigo is null && descripcion.Length == 0) continue;

            if (seccion is null)
            {
                seccion = new SeccionEstructuraImport { Indice = analisis.Secciones.Count, FilaDesde = fila.Numero };
                arbol.Reiniciar();
            }
            AnalizarFila(seccion, arbol, fila, codigo, descripcion, textoItem, siguienteCodigo[i], ctx);
        }

        // Si quedó una sección abierta, se procesó al menos una fila: datos no está vacío.
        if (seccion is not null)
            CerrarSeccion(analisis, seccion, datos[^1].Numero, ctx.ContratoNumeros);

        if (analisis.Secciones.Count == 0)
            return new AnalisisEstructura
            {
                Layout = enc.Layout,
                CodigoBloque = codigoBloque,
                NombreBloque = nombreBloque,
                ColumnasDetectadas = analisis.ColumnasDetectadas,
                Error = "La hoja no tiene ítems para importar debajo del encabezado.",
                SinItems = true
            };

        return analisis;
    }

    /// <summary>
    /// Lo que vale para toda la hoja: columnas, layout y grupo del balance. ContratoNumeros:
    /// balance con un grupo que no es Contrato, donde las columnas del contrato completan el
    /// PU del ítem y deciden su tipo de movimiento.
    /// </summary>
    private sealed record Contexto(Columnas Col, LayoutEstructura Layout, GrupoBalance Grupo, bool ContratoNumeros);

    /// <summary>
    /// Siguiente código de cada fila, en una pasada hacia atrás: la regla de prefijo lo
    /// consulta en cada fila y buscarlo hacia adelante cada vez sería cuadrático.
    /// </summary>
    private static string?[] SiguientesCodigos(IReadOnlyList<string?> codigos)
    {
        var siguiente = new string?[codigos.Count];
        string? ultimo = null;
        for (var i = codigos.Count - 1; i >= 0; i--)
        {
            siguiente[i] = ultimo;
            if (codigos[i] is not null) ultimo = codigos[i];
        }
        return siguiente;
    }

    /// <summary>
    /// Descripción de la fila y el texto de la columna Item. Sin código ni descripción, la
    /// descripción es lo que diga la columna Item; sin letras ni dígitos («-», «=») queda vacía.
    /// </summary>
    private static (string Descripcion, string TextoItem) LeerDescripcion(FilaExcel fila, Columnas col, string? codigo)
    {
        var descripcion = fila[col.Descripcion].Texto;
        var textoItem = fila[col.Item].Texto;
        if (codigo is null && descripcion.Length == 0) descripcion = textoItem;
        if (!descripcion.Any(char.IsLetterOrDigit)) descripcion = string.Empty;
        return (descripcion, textoItem);
    }

    // ── Fila de datos ────────────────────────────────────────────────────────────

    /// <summary>
    /// Clasifica una fila de datos (separador, código sin descripción, rubro, sub-rubro o
    /// ítem hoja) y la agrega a la sección con su lugar en el árbol.
    /// </summary>
    private static void AnalizarFila(SeccionEstructuraImport seccion, ArbolDeSeccion arbol, FilaExcel fila,
        string? codigo, string descripcion, string textoItem, string? siguienteCodigo, Contexto ctx)
    {
        var f = new FilaEstructuraImport { Fila = fila.Numero, Codigo = codigo, Descripcion = descripcion };
        f.Descripcion = Recortar(f, f.Descripcion, LargoTexto.Descripcion, "Descripción recortada");

        if (EsSeparador(descripcion) || EsSeparador(textoItem))
        {
            f.Tipo = TipoFilaEstructura.Ignorada;
            f.Detalle = "Separador de sección";
            AgregarFila(seccion, f);
            arbol.Reiniciar();
            return;
        }

        if (descripcion.Length == 0)
        {
            f.Tipo = TipoFilaEstructura.Ignorada;
            f.Detalle = "Código sin descripción";
            AgregarFila(seccion, f);
            return;
        }

        var numeros = LeerNumeros(fila, ctx);
        switch (TipoDeAgrupador(codigo, siguienteCodigo, numeros))
        {
            case TipoFilaEstructura.Rubro:
                f.Tipo = TipoFilaEstructura.Rubro;
                f.PadreIndice = arbol.PadreDeRubro(codigo);
                arbol.AbrirRubro(AgregarFila(seccion, f), f);
                return;
            case TipoFilaEstructura.SubRubro:
                f.Tipo = TipoFilaEstructura.SubRubro;
                f.PadreIndice = arbol.PadreDeSubRubro();
                arbol.AbrirSubRubro(AgregarFila(seccion, f), f);
                return;
        }

        f.PadreIndice = arbol.PadreDeItem(codigo);
        CompletarItem(f, fila, numeros, ctx);
        AgregarFila(seccion, f);
    }

    /// <summary>
    /// Unidad y números de una fila (null = celda vacía o no numérica). TieneNumeros: los
    /// rubros y sub-rubros no tienen números, y un error de Excel en uno cuenta como número.
    /// </summary>
    private sealed record NumerosFila(
        string Unidad, decimal? Cantidad, decimal? PU, decimal? Subtotal, bool TotalNumerico,
        decimal? CantidadContrato, decimal? PUContrato, IReadOnlyList<string> Errores, bool TieneNumeros);

    private static NumerosFila LeerNumeros(FilaExcel fila, Contexto ctx)
    {
        var col = ctx.Col;
        var cantidad = col.Cantidad is int cc ? fila[cc].Numero : null;
        var pu = col.PU is int cp ? fila[cp].Numero : null;
        var cantidadContrato = col.CantidadContrato is int ccc ? fila[ccc].Numero : null;
        var puContrato = col.PUContrato is int cpc ? fila[cpc].Numero : null;
        // Un error de Excel (#REF!, #DIV/0!) en un número del ítem no es una celda vacía:
        // la fila sigue siendo un ítem (los rubros no tienen números) y se avisa.
        var errores = ErroresNumericos(fila, ctx);

        return new NumerosFila(
            Unidad: col.Unidad is int cu ? fila[cu].Texto : string.Empty,
            Cantidad: cantidad,
            PU: pu,
            Subtotal: col.Subtotal is int cs ? fila[cs].Numero : null,
            // Solo un número en TOTAL marca rubro: un «-» o un texto en esa celda (ítems «No
            // aplica» de algunas contratistas) no debe convertir el ítem en agrupador.
            TotalNumerico: col.Total is int ct && fila[ct].Numero is not null,
            CantidadContrato: cantidadContrato,
            PUContrato: puContrato,
            Errores: errores,
            TieneNumeros: cantidad.HasValue || pu.HasValue || errores.Count > 0
                          || (ctx.ContratoNumeros && (cantidadContrato.HasValue || puContrato.HasValue)));
    }

    /// <summary>
    /// Rubro, sub-rubro o null (ítem hoja). Rubro: sin números y, o bien sin unidad y con un
    /// número en TOTAL (un «No aplica» con unidad y TOTAL 0 es un ítem sin monto, no un
    /// agrupador que capture a los ítems que le siguen), o bien su código es prefijo (más un
    /// punto) del siguiente código. Sub-rubro: sin código, sin números y sin unidad.
    /// </summary>
    private static TipoFilaEstructura? TipoDeAgrupador(string? codigo, string? siguienteCodigo, NumerosFila numeros)
    {
        if (numeros.TieneNumeros) return null;
        if ((numeros.TotalNumerico && numeros.Unidad.Length == 0)
            || (codigo is not null && siguienteCodigo is not null
                && siguienteCodigo.StartsWith(codigo + ".", StringComparison.OrdinalIgnoreCase)))
            return TipoFilaEstructura.Rubro;
        return codigo is null && numeros.Unidad.Length == 0 ? TipoFilaEstructura.SubRubro : null;
    }

    /// <summary>Agrega la fila a la sección con el nivel que le da su padre (raíz = 0) y devuelve su índice.</summary>
    private static int AgregarFila(SeccionEstructuraImport seccion, FilaEstructuraImport f)
    {
        f.Nivel = f.PadreIndice is int p ? seccion.Filas[p].Nivel + 1 : 0;
        seccion.Filas.Add(f);
        return seccion.Filas.Count - 1;
    }

    /// <summary>
    /// Recorta un texto al largo de su columna en la base, con aviso: una celda desbordada
    /// no debe hacer fallar la importación entera al guardar.
    /// </summary>
    private static string Recortar(FilaEstructuraImport f, string texto, int largo, string aviso)
    {
        if (texto.Length <= largo) return texto;
        f.AgregarDetalle($"{aviso} a {largo} caracteres");
        return texto[..largo];
    }

    /// <summary>
    /// Columnas numéricas del ítem con un error de Excel, ya redactadas para el aviso
    /// («Cantidad con error de Excel (#REF!)»). El subtotal cuenta solo en cotización, donde
    /// es la fuente del precio unitario; las del contrato, solo cuando completan el grupo.
    /// </summary>
    private static List<string> ErroresNumericos(FilaExcel fila, Contexto ctx)
    {
        var col = ctx.Col;
        var errores = new List<string>();
        void Revisar(int? columna, string nombre)
        {
            if (columna is int c && fila[c].Error is string codigo)
                errores.Add($"{nombre} con error de Excel ({codigo})");
        }
        Revisar(col.Cantidad, "Cantidad");
        Revisar(col.PU, "PU");
        if (ctx.Layout == LayoutEstructura.Cotizacion) Revisar(col.Subtotal, "Subtotal");
        if (ctx.ContratoNumeros)
        {
            Revisar(col.CantidadContrato, "Cantidad del contrato");
            Revisar(col.PUContrato, "PU del contrato");
        }
        return errores;
    }

    // ── Ítem hoja ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Completa un ítem hoja: código y unidad recortados al largo de su columna, avisos de
    /// errores de Excel, cantidad, PU y monto según el layout, y su tipo final. En el balance,
    /// sin cantidad en el grupo elegido no es ítem: queda como economía o ignorado.
    /// </summary>
    private static void CompletarItem(FilaEstructuraImport f, FilaExcel fila, NumerosFila numeros, Contexto ctx)
    {
        var codigo = f.Codigo;
        if (codigo is not null) f.Codigo = Recortar(f, codigo, LargoTexto.PorDefecto, "Código recortado");
        f.Unidad = numeros.Unidad.Length > 0 ? Recortar(f, numeros.Unidad, LargoTexto.PorDefecto, "Unidad recortada") : null;
        foreach (var error in numeros.Errores)
            f.AgregarDetalle($"{error}: se tomó como vacío");

        if (ctx.ContratoNumeros && (numeros.Cantidad is null || numeros.Cantidad == 0))
        {
            var economia = ctx.Col.CantidadEconomia is int ce ? fila[ce].Numero : null;
            f.Tipo = economia is not null && economia != 0 ? TipoFilaEstructura.Economia : TipoFilaEstructura.Ignorada;
            f.AgregarDetalle(f.Tipo == TipoFilaEstructura.Economia
                ? "Economía: no se importa como ítem"
                : $"Sin cantidad en el grupo {ctx.Grupo}");
            return;
        }

        var (pu, columnaPU, rechazo) = PrecioUnitario(numeros, ctx);
        if (rechazo is not null)
        {
            f.Tipo = TipoFilaEstructura.Ignorada;
            f.AgregarDetalle($"{rechazo} No se importa: corregí el valor en la planilla");
            return;
        }

        AsignarNumeros(f, fila, numeros.Cantidad, pu, columnaPU, ctx);
        if (ctx.ContratoNumeros)
            f.TipoMovimiento = TipoMovimientoDelBalance(ctx.Grupo, numeros.CantidadContrato, codigo);
        ClasificarItem(f, conCodigo: codigo is not null);
    }

    /// <summary>
    /// PU del ítem y la columna de la que sale, que es la que mira el aviso de texto: la
    /// propia, el Subtotal ÷ Cantidad en la cotización o el PU del contrato en el balance.
    /// Rechazo: el motivo si la cantidad o el PU son negativos, o si cantidad, PU o monto no
    /// entran en la base (con las mismas reglas que el alta manual: ver EstructuraValidator.Valores).
    /// </summary>
    private static (decimal? PU, int? Columna, string? Rechazo) PrecioUnitario(NumerosFila numeros, Contexto ctx)
    {
        var pu = numeros.PU;
        var columna = ctx.Col.PU;
        string? fueraDeRango = null;
        if (ctx.Layout == LayoutEstructura.Cotizacion && pu is null
            && numeros.Cantidad is decimal cantidad && cantidad != 0 && numeros.Subtotal is decimal subtotal)
        {
            columna = ctx.Col.Subtotal;
            // Una cantidad ínfima hace desbordar el cociente: es un dato fuera de rango,
            // no una falla de la hoja entera.
            try { pu = subtotal / cantidad; }
            catch (OverflowException) { fueraDeRango = "El PU (Subtotal ÷ Cantidad) supera el máximo admitido."; }
        }
        if (ctx.ContratoNumeros && pu is null && numeros.PUContrato is not null)
        {
            pu = numeros.PUContrato;
            columna = ctx.Col.PUContrato;
        }
        return (pu, columna, fueraDeRango ?? EstructuraValidator.Valores(numeros.Cantidad, pu));
    }

    /// <summary>
    /// Cantidad y PU con los 4 decimales que guarda la base (si la planilla trae más, como
    /// 6,88905 m3, el monto difiere del subtotal de la planilla y el detalle lo dice) y el
    /// monto con la regla de la app. Un número tipeado como texto se parsea con la regla
    /// INDEC (sin coma = punto decimal): «1.500» se leería 1,5, así que se avisa.
    /// </summary>
    private static void AsignarNumeros(FilaEstructuraImport f, FilaExcel fila,
        decimal? cantidad, decimal? pu, int? columnaPU, Contexto ctx)
    {
        f.Cantidad = cantidad is null ? null : Math.Round(cantidad.Value, 4);
        f.PUBasico = pu is null ? null : Math.Round(pu.Value, 4);
        if (cantidad is not null && f.Cantidad != cantidad) f.AgregarDetalle("Cantidad redondeada a 4 decimales");
        if (pu is not null && f.PUBasico != pu && ctx.Layout != LayoutEstructura.Cotizacion) f.AgregarDetalle("PU redondeado a 4 decimales");
        if ((ctx.Col.Cantidad is int cc && fila[cc].EsTexto) || (columnaPU is int cp && fila[cp].EsTexto))
            f.AgregarDetalle("Cantidad o PU leídos de una celda de texto: verificar el separador decimal");
        f.Monto = EstructuraValidator.CalcularMonto(false, f.Cantidad, f.PUBasico);
    }

    /// <summary>
    /// Tipo de movimiento en el balance: con el grupo Demasía, un ítem sin cantidad de
    /// contrato o con código «N-…» es Adicional y el resto Demasía; con otro grupo, ninguno.
    /// </summary>
    private static TipoMovimiento? TipoMovimientoDelBalance(GrupoBalance grupo, decimal? cantidadContrato, string? codigo) =>
        grupo == GrupoBalance.Demasia
            ? (cantidadContrato is null || (codigo?.StartsWith("N-", StringComparison.OrdinalIgnoreCase) ?? false)
                ? TipoMovimiento.Adicional
                : TipoMovimiento.Demasia)
            : null;

    /// <summary>Tipo final del ítem hoja, con su aviso: sin código, sin monto, monto 0 o ítem completo.</summary>
    private static void ClasificarItem(FilaEstructuraImport f, bool conCodigo)
    {
        if (!conCodigo)
        {
            f.Tipo = TipoFilaEstructura.ItemSinCodigo;
            f.AgregarDetalle("Ítem sin código");
        }
        else if (f.Monto is null)
        {
            f.Tipo = TipoFilaEstructura.ItemSinMonto;
            f.AgregarDetalle("Sin cantidad o sin precio: se importa con monto vacío");
        }
        else if (f.Monto == 0)
        {
            f.Tipo = TipoFilaEstructura.ItemSinMonto;
            f.AgregarDetalle("Cantidad o precio en cero: se importa con monto 0");
        }
        else
        {
            f.Tipo = TipoFilaEstructura.Item;
        }
    }

    // ── Árbol de la sección ──────────────────────────────────────────────────────

    /// <summary>
    /// Agrupadores abiertos mientras se recorre una sección: la pila de rubros y sub-rubros
    /// de la que cuelga la fila actual, y los rubros con código ya vistos (código → índice)
    /// para los rubros e ítems que aparecen fuera de orden. Se reinicia al abrir una sección
    /// y en cada separador.
    /// </summary>
    private sealed class ArbolDeSeccion
    {
        private readonly Stack<(int Indice, FilaEstructuraImport Fila)> pila = new();
        private readonly Dictionary<string, int> rubrosPorCodigo = new(StringComparer.OrdinalIgnoreCase);

        public void Reiniciar()
        {
            pila.Clear();
            rubrosPorCodigo.Clear();
        }

        /// <summary>
        /// Padre de un rubro codificado: el rubro cuyo código es prefijo del suyo, el abierto
        /// más cercano o, si aparece fuera de orden (A.1.5 después de A.2), el ya visto con el
        /// prefijo más largo. Sin código (ítems nuevos del balance) nace en la raíz. Cierra
        /// los agrupadores abiertos que no lo contienen (los sub-rubros, siempre).
        /// </summary>
        public int? PadreDeRubro(string? codigo)
        {
            while (pila.Count > 0 && !(codigo is not null && pila.Peek().Fila.Codigo is string pc
                                        && codigo.StartsWith(pc + ".", StringComparison.OrdinalIgnoreCase)))
                pila.Pop();
            return (codigo is null ? null : RubroPrefijo(codigo))
                   ?? (pila.Count > 0 ? pila.Peek().Indice : null);
        }

        public void AbrirRubro(int indice, FilaEstructuraImport rubro)
        {
            pila.Push((indice, rubro));
            if (rubro.Codigo is not null) rubrosPorCodigo[rubro.Codigo] = indice;
        }

        /// <summary>Padre de un sub-rubro: el rubro abierto (dos sub-rubros seguidos quedan como hermanos).</summary>
        public int? PadreDeSubRubro()
        {
            while (pila.Count > 0 && pila.Peek().Fila.Tipo == TipoFilaEstructura.SubRubro)
                pila.Pop();
            return pila.Count > 0 ? pila.Peek().Indice : null;
        }

        public void AbrirSubRubro(int indice, FilaEstructuraImport subRubro) => pila.Push((indice, subRubro));

        /// <summary>
        /// Padre de un ítem hoja: el agrupador abierto, salvo que su código diga otra cosa. Si
        /// el agrupador con código más cercano no es prefijo del suyo y un rubro ya visto sí lo
        /// es, cuelga de ese: ítems agregados fuera de orden, como el 01.10.29 que en las hojas
        /// de certificado de Agüero aparece después del rubro 01.11.
        /// </summary>
        public int? PadreDeItem(string? codigo)
        {
            if (codigo is not null
                && pila.Select(x => x.Fila.Codigo).FirstOrDefault(c => c is not null) is string cercano
                && !codigo.StartsWith(cercano + ".", StringComparison.OrdinalIgnoreCase)
                && RubroPrefijo(codigo) is int rubro)
                return rubro;
            return pila.Count > 0 ? pila.Peek().Indice : null;
        }

        /// <summary>Rubro ya visto cuyo código es el prefijo más largo del dado («A.1.2» → «A.1» → «A»), o null.</summary>
        private int? RubroPrefijo(string codigo)
        {
            for (var corte = codigo.LastIndexOf('.'); corte > 0; corte = codigo.LastIndexOf('.', corte - 1))
                if (rubrosPorCodigo.TryGetValue(codigo[..corte], out var indice)) return indice;
            return null;
        }
    }

    // ── Cierre de sección ────────────────────────────────────────────────────────

    private static void CerrarSeccion(AnalisisEstructura analisis, SeccionEstructuraImport seccion, int filaHasta, bool soloAgrupadoresConItems)
    {
        seccion.FilaHasta = filaHasta;

        if (soloAgrupadoresConItems)
        {
            // Balance: la mayoría de los rubros del contrato no tienen demasías; un
            // rubro sin ningún ítem importado debajo no se crea en la estructura BED.
            var conItems = new bool[seccion.Filas.Count];
            for (var i = 0; i < seccion.Filas.Count; i++)
            {
                var f = seccion.Filas[i];
                if (!f.SeImporta || f.EsAgrupador) continue;
                for (var p = f.PadreIndice; p is int pi; p = seccion.Filas[pi].PadreIndice)
                    conItems[pi] = true;
            }
            for (var i = 0; i < seccion.Filas.Count; i++)
            {
                var f = seccion.Filas[i];
                if (f.EsAgrupador && !conItems[i])
                {
                    f.Tipo = TipoFilaEstructura.Ignorada;
                    f.Detalle = "Rubro sin ítems en el grupo elegido";
                }
            }
        }

        // Códigos repetidos: se importan igual (no hay unicidad en la app) pero se avisan,
        // porque después hacen ambiguo el matching de los certificados.
        var repetidos = seccion.ItemsAImportar
            .Where(f => f.Codigo is not null)
            .GroupBy(f => TextoImport.ClaveCodigo(f.Codigo))
            .Where(g => g.Count() > 1);
        foreach (var g in repetidos)
            foreach (var f in g)
                f.AgregarDetalle("Código repetido en la planilla");

        if (seccion.CantidadItems == 0) return;

        var primera = seccion.Filas.First(f => f.SeImporta);
        seccion.Titulo = string.Join(" ", new[] { primera.Codigo, primera.Descripcion }.Where(s => !string.IsNullOrEmpty(s)));
        analisis.Secciones.Add(seccion);
    }

    // ── Encabezado y columnas ────────────────────────────────────────────────────

    private static Encabezado? DetectarEncabezado(HojaExcel hoja, GrupoBalance grupo)
    {
        for (var i = 0; i < Math.Min(hoja.Filas.Count, TextoImport.FilasDeBusquedaDeEncabezado); i++)
        {
            var fila = hoja.Filas[i];
            var t = TextoImport.Normalizadas(fila);
            if (!TextoImport.EsFilaDeEncabezado(t, out var item, out var descripcion)) continue;

            var gEco = Array.FindIndex(t, x => x == "economia");
            var gDem = Array.FindIndex(t, x => x == "demasia");
            var gBed = Array.FindIndex(t, x => x == "bed");
            if (gDem >= 0 || gEco >= 0)
            {
                // Balance: los rótulos de grupo van en el encabezado y la Cantidad, el Valor
                // Unitario y el Subtotal de cada grupo, en la fila de abajo.
                if (TextoImport.FilaSiguiente(hoja, i) is not FilaExcel sub) continue;
                var s = TextoImport.Normalizadas(sub);

                var cantidadContrato = Array.FindIndex(t, x => x.StartsWith("cantidad"));
                var puContrato = Array.FindIndex(t, x => x.Contains("valor unitario") || x.Contains("precio unitario"));
                var totalRubro = Array.FindIndex(t, x => x.Contains("total rubro"));
                var totalItem = Array.FindIndex(t, x => x.Contains("total item") || x.Contains("subtotal"));
                var unidad = Array.FindIndex(t, TextoImport.EsEncabezadoUnidad);

                int? cantidad, pu, subtotal;
                var inicio = grupo switch
                {
                    GrupoBalance.Demasia => gDem,
                    GrupoBalance.Economia => gEco,
                    GrupoBalance.Bed => gBed,
                    _ => -1
                };
                if (grupo == GrupoBalance.Contrato)
                {
                    // Sin Cantidad del contrato todos los ítems saldrían «sin monto» en
                    // silencio: se avisa igual que cuando falta la del grupo elegido.
                    if (cantidadContrato < 0)
                        return new Encabezado(null, LayoutEstructura.Balance, sub.Numero,
                            $"El grupo «{NombreGrupo(grupo)}» no tiene la columna Cantidad en el encabezado.");
                    cantidad = cantidadContrato;
                    pu = TextoImport.Opcional(puContrato);
                    subtotal = TextoImport.Opcional(totalItem);
                }
                else
                {
                    // El layout es balance pero falta el grupo pedido: se informa eso y no
                    // «no se encontró el encabezado», que mandaría al usuario a buscar otra hoja.
                    if (inicio < 0)
                        return new Encabezado(null, LayoutEstructura.Balance, sub.Numero,
                            $"La hoja es un balance pero no tiene el grupo de columnas «{NombreGrupo(grupo)}». Elegí otro grupo.");
                    (cantidad, pu, subtotal) = ColumnasDeGrupo(s, inicio);
                    if (cantidad is null)
                        return new Encabezado(null, LayoutEstructura.Balance, sub.Numero,
                            $"El grupo «{NombreGrupo(grupo)}» no tiene la columna Cantidad en la fila siguiente al encabezado.");
                }
                var (cantidadEconomia, _, _) = gEco >= 0 ? ColumnasDeGrupo(s, gEco) : (null, null, null);

                return new Encabezado(
                    new Columnas(item, descripcion, TextoImport.Opcional(unidad), cantidad, pu, subtotal, TextoImport.Opcional(totalRubro),
                        TextoImport.Opcional(cantidadContrato), TextoImport.Opcional(puContrato), cantidadEconomia),
                    LayoutEstructura.Balance, sub.Numero);
            }

            var u = Array.FindIndex(t, TextoImport.EsEncabezadoUnidad);
            var cant = Array.FindIndex(t, x => x.StartsWith("cantidad") || x.StartsWith("cant"));
            var precio = Array.FindIndex(t, x => (x.Contains("precio unitario") || x.Contains("valor unitario")) && !EsUsd(x));
            var subt = Array.FindIndex(t, x => x.Contains("subtotal") && !EsUsd(x));
            var total = Array.FindIndex(t, x => x is "total" or "total $" or "total rubro");

            var layout = precio < 0 ? LayoutEstructura.Cotizacion : LayoutEstructura.Desglose;
            return new Encabezado(
                new Columnas(item, descripcion, TextoImport.Opcional(u), TextoImport.Opcional(cant), TextoImport.Opcional(precio),
                    TextoImport.Opcional(subt), TextoImport.Opcional(total), null, null, null),
                layout, fila.Numero);
        }
        return null;
    }

    /// <summary>Cantidad, valor unitario y subtotal del grupo que empieza en la columna <paramref name="inicio"/> del subencabezado.</summary>
    private static (int? Cantidad, int? PU, int? Subtotal) ColumnasDeGrupo(string[] subEncabezado, int inicio)
    {
        var cantidad = TextoImport.Buscar(subEncabezado, inicio, x => x.StartsWith("cantidad"));
        if (cantidad < 0) return (null, null, null);
        var pu = TextoImport.Buscar(subEncabezado, cantidad + 1, x => x.Contains("valor unitario") || x.Contains("precio unitario"));
        var subtotal = pu < 0 ? -1 : TextoImport.Buscar(subEncabezado, pu + 1, x => x.Contains("subtotal"));
        return (cantidad, TextoImport.Opcional(pu), TextoImport.Opcional(subtotal));
    }

    private static string NombreGrupo(GrupoBalance grupo) => grupo switch
    {
        GrupoBalance.Demasia => "DEMASIA",
        GrupoBalance.Economia => "ECONOMIA",
        GrupoBalance.Bed => "BED",
        _ => "Contrato"
    };

    private static bool EsUsd(string x) => x.Contains("usd") || x.Contains("us$") || x.Contains("u$s");

    private static string? LeerCodigo(FilaExcel fila, Columnas col)
    {
        var texto = fila[col.Item].Texto;
        return TextoImport.PareceCodigo(texto) ? texto : null;
    }

    private static bool EsSeparador(string texto)
    {
        var n = TextoImport.Normalizar(texto);
        return n.Length > 0 && Separadores.Contains(n);
    }

    /// <summary>
    /// Fila de cierre de sección. Dos formas: (1) «TOTAL …» en la columna del código o en
    /// alguna numérica (según la contratista va en Item, Precio Unitario o Cantidad),
    /// nunca como código y sin cantidad numérica; (2) un subtotal sin la palabra: código
    /// y descripción vacíos, un texto en Cantidad/PU/Subtotal/Total y un número en
    /// Subtotal o Total (la cotización de Lacroze cierra cada bloque con su nombre en la
    /// columna Cantidad). La descripción no se mira: un ítem puede decir «total».
    /// </summary>
    private static bool EsFilaTotal(FilaExcel fila, Columnas col)
    {
        if (col.Cantidad is int cc && fila[cc].Numero is not null) return false;
        if (ColumnasDeTexto(col).Any(c => TextoImport.EsTextoDeTotal(fila[c].Texto))) return true;

        if (fila[col.Item].Texto.Length > 0 || fila[col.Descripcion].Texto.Length > 0) return false;
        var hayNumero = (col.Subtotal is int cs && fila[cs].Numero is not null)
                        || (col.Total is int ct && fila[ct].Numero is not null);
        if (!hayNumero) return false;
        foreach (var c in new[] { col.Cantidad, col.PU, col.Subtotal, col.Total })
        {
            if (c is not int i) continue;
            var texto = fila[i].Texto;
            if (fila[i].Numero is null && texto.Count(char.IsLetter) >= 4) return true;
        }
        return false;
    }

    private static IEnumerable<int> ColumnasDeTexto(Columnas col)
    {
        yield return col.Item;
        foreach (var c in new[] { col.Unidad, col.Cantidad, col.PU, col.Subtotal, col.Total, col.CantidadContrato, col.PUContrato })
            if (c is int i) yield return i;
    }

    private static decimal? LeerTotal(FilaExcel fila, Columnas col)
    {
        foreach (var c in new[] { col.Subtotal, col.Total, col.PU })
            if (c is int i && fila[i].Numero is decimal v) return v;
        return fila.Celdas.Select(c => c.Numero).FirstOrDefault(n => n is not null);
    }

    private static (string? Codigo, string? Nombre) LeerBloque(HojaExcel hoja, int filaEncabezado, Columnas col)
    {
        var fila = hoja.Filas
            .Where(f => f.Numero < filaEncabezado)
            .LastOrDefault(f => TextoImport.PareceCodigo(f[col.Item].Texto) && f[col.Descripcion].Texto.Length > 0);
        return fila is null ? (null, null) : (fila[col.Item].Texto, fila[col.Descripcion].Texto);
    }

    private static string DescribirColumnas(Columnas col, LayoutEstructura layout)
    {
        var partes = new List<string>
        {
            $"Código={Letra(col.Item)}", $"Descripción={Letra(col.Descripcion)}"
        };
        if (col.Unidad is int u) partes.Add($"U={Letra(u)}");
        if (col.Cantidad is int c) partes.Add($"Cantidad={Letra(c)}");
        if (col.PU is int p) partes.Add($"PU={Letra(p)}");
        else if (layout == LayoutEstructura.Cotizacion) partes.Add("PU=Subtotal÷Cantidad");
        if (col.Subtotal is int s) partes.Add($"Subtotal={Letra(s)}");
        if (col.Total is int t) partes.Add($"Total={Letra(t)}");
        return string.Join(", ", partes);
    }

    /// <summary>Índice 0-based a letra de columna de Excel (0 = A, 26 = AA), con el helper de ClosedXML.</summary>
    private static string Letra(int columna) => XLHelper.GetColumnLetterFromNumber(columna + 1);
}
