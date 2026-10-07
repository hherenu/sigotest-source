using System.Text.RegularExpressions;
using SIGO.Models.ViewModels;

namespace SIGO.Services.Importacion;

/// <summary>Renglón de ítem leído de la hoja de certificado (rubros y sub-rubros no se listan).</summary>
public sealed class FilaCertificadoImport
{
    public int Fila { get; init; }
    public string Codigo { get; init; } = string.Empty;
    public string Descripcion { get; init; } = string.Empty;
    /// <summary>% anterior según la planilla (ya en puntos porcentuales), para contrastar con el sistema.</summary>
    public decimal? PctAnterior { get; init; }
    /// <summary>% del mes según la planilla, en puntos porcentuales. Null = celda vacía (0 %).</summary>
    public decimal? PctActual { get; init; }
    /// <summary>
    /// La celda Actual no está vacía pero no da un % («s/d», un error de Excel o un valor
    /// absurdo): el renglón no se aplica.
    /// </summary>
    public bool ActualIlegible { get; init; }
    /// <summary>Monto del mes según la planilla (columna Actual del certificado), para contrastar por renglón.</summary>
    public decimal? MontoActualPlanilla { get; init; }
}

/// <summary>
/// Sección de la hoja: un título, un encabezado (propio o heredado), sus renglones y
/// la fila TOTAL que la cierra. Cada estación o el bloque DEMASIAS es una sección.
/// </summary>
public sealed class SeccionCertificadoImport
{
    public int Indice { get; init; }
    public string Titulo { get; set; } = string.Empty;
    public int FilaDesde { get; init; }
    public int FilaHasta { get; set; }
    public List<FilaCertificadoImport> Filas { get; } = [];
    /// <summary>Monto del mes de la fila TOTAL que cierra la sección (null si termina sin total).</summary>
    public decimal? TotalMontoActualPlanilla { get; set; }

    public int ConPorcentaje => Filas.Count(f => f.PctActual is not null && f.PctActual != 0);
}

public sealed class AnalisisCertificado
{
    /// <summary>Texto «CERTIFICADO N°… - período» de la cabecera, si se encontró.</summary>
    public string? Encabezado { get; init; }
    public int? Numero { get; init; }
    public int? Mes { get; init; }
    public int? Anio { get; init; }
    public List<SeccionCertificadoImport> Secciones { get; } = [];
    public string? Error { get; init; }

    /// <summary>true si la cabecera leída es la del certificado N° <paramref name="numero"/> de <paramref name="mes"/>/<paramref name="anio"/>.</summary>
    public bool CoincideCon(int numero, int mes, int anio) => Numero == numero && Mes == mes && Anio == anio;
}

public enum EstadoFilaCertificado { Coincide, AnteriorDistinto, CodigoDesconocido, CodigoRepetido, ValorIlegible }

public sealed record DetalleFilaCertificado(FilaCertificadoImport Fila, EstadoFilaCertificado Estado, decimal? PctAnteriorSistema);

/// <summary>Resultado de aplicar una sección de la planilla sobre un bloque del certificado.</summary>
public sealed class AplicacionBloque
{
    public int CertificadoEstructuraId { get; init; }
    public string TituloBloque { get; init; } = string.Empty;
    /// <summary>ItemEstructuraId → % del mes a volcar en la grilla.</summary>
    public Dictionary<int, decimal> Porcentajes { get; } = [];
    public List<DetalleFilaCertificado> Detalle { get; } = [];
    public List<ItemCertificadoVM> ItemsSinFila { get; } = [];
    public decimal TotalMontoMesCalculado { get; set; }
    public decimal? TotalMontoMesPlanilla { get; set; }

    public int Coincidentes => Detalle.Count(d => d.Estado is EstadoFilaCertificado.Coincide or EstadoFilaCertificado.AnteriorDistinto);
    public int Desconocidos => Detalle.Count(d => d.Estado == EstadoFilaCertificado.CodigoDesconocido);
    public int Repetidos => Detalle.Count(d => d.Estado == EstadoFilaCertificado.CodigoRepetido);
    public int Ilegibles => Detalle.Count(d => d.Estado == EstadoFilaCertificado.ValorIlegible);
    public int AnterioresDistintos => Detalle.Count(d => d.Estado == EstadoFilaCertificado.AnteriorDistinto);
    public IEnumerable<DetalleFilaCertificado> Problemas => Detalle.Where(d => d.Estado != EstadoFilaCertificado.Coincide);
}

/// <summary>
/// Una hoja del libro evaluada para la importación: su análisis (null si no tiene encabezado
/// de certificado), el texto del desplegable (N° y período leídos, «sin renglones», «oculta»)
/// y, si no se pudo analizar, la excepción, para que la interfaz la registre.
/// </summary>
public sealed record HojaCertificadoOpcion(HojaExcel Hoja, AnalisisCertificado? Analisis, string Etiqueta, Exception? Falla = null)
{
    /// <summary>Valor del desplegable: Excel no admite dos hojas con el mismo nombre.</summary>
    public string Nombre => Hoja.Nombre;
    public bool Utilizable => Analisis is { Error: null };
    public int Renglones => Analisis?.Secciones.Sum(s => s.Filas.Count) ?? 0;
    public int ConPorcentaje => Analisis?.Secciones.Sum(s => s.ConPorcentaje) ?? 0;
}

/// <summary>
/// Resultado de <see cref="ImportadorCertificado.EvaluarHojas"/>. <c>Alternativas</c>: otras hojas
/// utilizables con el mismo encabezado que la elegida y una cantidad parecida de renglones
/// (versiones paralelas o viejas del mismo certificado), que la interfaz avisa.
/// </summary>
public sealed record EleccionHojaCertificado(
    IReadOnlyList<HojaCertificadoOpcion> Opciones,
    HojaCertificadoOpcion? Elegida,
    IReadOnlyList<HojaCertificadoOpcion> Alternativas);

/// <summary>
/// Parseo puro de la hoja CERTIFICADO que arma CCyR: columnas Item, Descripción, U,
/// Cant., Precio Unitario, Precio Subtotal, ACTA DE MEDICIÓN (Anterior, Actual,
/// Acumulado, como fracción con formato %) y CERTIFICADO (montos). La hoja se corta en
/// secciones (título, encabezado, renglones, fila TOTAL) y cada sección se aplica sobre
/// un bloque del certificado matcheando por código dentro del bloque.
/// </summary>
public static partial class ImportadorCertificado
{
    private const decimal ToleranciaPct = 0.01m;
    /// <summary>Un % más allá de este valor no es un avance sino un dato roto (y desbordaría los montos).</summary>
    private const decimal PctMaximoLegible = 1000m;

    private sealed record Columnas(int Item, int Descripcion, int? Unidad, int? Cantidad, int? Subtotal,
        int? PctAnterior, int PctActual, int? MontoActual, int UltimaColumna);

    /// <summary>Encabezado detectado: sus columnas y cuántas filas ocupa (2 si Anterior/Actual van debajo).</summary>
    private sealed record Encabezado(Columnas Columnas, int FilasOcupadas);

    // «N°01», «Nº 7», «N.° 5», «Nro 5», «Nro. 5», «No 5», «N 5».
    [GeneratedRegex(@"certificado.*?\bn(?:ro|o|°|º|\.°|\.º)?\.?\s*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex NumeroRegex();

    // «MAY/25», «MAYO 2025», «JUNIO DE 2026».
    [GeneratedRegex(@"([A-Za-zÁÉÍÓÚáéíóú]{3,10})\.?\s*[/\-\s]\s*(?:[dD][eE]\s+)?(\d{4}|\d{2})\b")]
    private static partial Regex PeriodoRegex();

    private static readonly string[] Meses =
        ["ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic"];

    /// <summary>true si la hoja tiene un encabezado de certificado (Item + Descripción + columna Actual).</summary>
    public static bool EsHojaDeCertificado(HojaExcel hoja) => BuscarPrimerEncabezado(hoja) is not null;

    /// <summary>
    /// Evalúa las hojas del libro y elige cuál preseleccionar para el certificado N°
    /// <paramref name="numero"/> de <paramref name="mes"/>/<paramref name="anio"/>. Los libros
    /// reales traen varias con el encabezado del certificado: un resumen sin renglones
    /// (Premetro), copias ocultas de meses anteriores y hojas paralelas en cero (Agüero desde
    /// el BED). Criterio, en orden: que se analice sin error, que coincida en N° y período,
    /// que esté visible, que tenga más renglones y más % del mes cargados. Una hoja que no se
    /// puede analizar queda en la lista como «no se pudo analizar» y no impide usar las demás.
    /// </summary>
    public static EleccionHojaCertificado EvaluarHojas(LibroExcel libro, int numero, int mes, int anio)
    {
        var opciones = libro.Hojas.Select(h =>
        {
            try
            {
                var analisis = EsHojaDeCertificado(h) ? Analizar(h) : null;
                return new HojaCertificadoOpcion(h, analisis, Etiqueta(h, analisis));
            }
            catch (Exception ex)
            {
                var fallida = new AnalisisCertificado
                {
                    Error = $"No se pudo analizar la hoja «{h.Nombre}»: ocurrió un error inesperado y quedó registrado. Probá con otra hoja."
                };
                return new HojaCertificadoOpcion(h, fallida, TextoImport.EtiquetaHoja(h, "no se pudo analizar"), ex);
            }
        }).ToList();

        // OrderBy es estable: a igualdad de criterios queda la primera del libro.
        var elegida = opciones.Where(o => o.Utilizable)
            .OrderByDescending(o => Coincidencia(o.Analisis!, numero, mes, anio))
            .ThenBy(o => o.Hoja.Oculta)
            .ThenByDescending(o => o.Renglones)
            .ThenByDescending(o => o.ConPorcentaje)
            .FirstOrDefault();

        List<HojaCertificadoOpcion> alternativas = elegida is null
            ? []
            : opciones.Where(o => !ReferenceEquals(o, elegida) && o.Utilizable
                                  && MismoEncabezado(o.Analisis!, elegida.Analisis!)
                                  && o.Renglones * 2 >= elegida.Renglones)
                .ToList();

        return new EleccionHojaCertificado(opciones, elegida, alternativas);
    }

    /// <summary>Número y período del texto «CERTIFICADO N°14 - JUNIO/26» (null en lo que no se reconozca).</summary>
    public static (int? Numero, int? Mes, int? Anio) ParsearEncabezado(string texto)
    {
        int? numero = null, mes = null, anio = null;
        var mNum = NumeroRegex().Match(texto);
        if (mNum.Success && int.TryParse(mNum.Groups[1].Value, out var n)) numero = n;

        // El período viene después del número: «N°01 - MAY/25», «N°20 - AGOSTO 2026».
        var resto = mNum.Success ? texto[(mNum.Index + mNum.Length)..] : texto;
        foreach (Match m in PeriodoRegex().Matches(resto))
        {
            var clave = TextoImport.Normalizar(m.Groups[1].Value);
            if (clave.StartsWith("set")) clave = "sep";
            var idx = Array.FindIndex(Meses, x => clave.StartsWith(x));
            if (idx < 0) continue;
            var a = int.Parse(m.Groups[2].Value);
            mes = idx + 1;
            anio = a < 100 ? 2000 + a : a;
            break;
        }
        return (numero, mes, anio);
    }

    public static AnalisisCertificado Analizar(HojaExcel hoja)
    {
        var primero = BuscarPrimerEncabezado(hoja);
        if (primero is null)
            return new AnalisisCertificado
            {
                Error = "No se encontró el encabezado del certificado (una fila con «Item», «Descripción» y la columna «Actual» del acta de medición)."
            };

        var (indicePrimerEncabezado, encabezadoInicial) = primero.Value;
        var col = encabezadoInicial.Columnas;

        // Cabecera: la primera fila (arriba del encabezado) que en la columna del código o
        // en alguna anterior dice «CERTIFICADO N°…». Se exige el número: un mes suelto
        // («Fecha de inicio: Marzo 2024») no es cabecera.
        string? encabezado = null;
        int? numero = null, mes = null, anio = null;
        for (var i = 0; i < indicePrimerEncabezado && encabezado is null; i++)
        {
            for (var c = 0; c <= col.Item; c++)
            {
                var texto = hoja.Filas[i][c].Texto;
                var (n, m, a) = ParsearEncabezado(texto);
                if (n is null) continue;
                encabezado = texto;
                numero = n; mes = m; anio = a;
                break;
            }
        }

        var analisis = new AnalisisCertificado { Encabezado = encabezado, Numero = numero, Mes = mes, Anio = anio };

        SeccionCertificadoImport? seccion = null;
        string? tituloPendiente = null;
        // Título del tramo vigente, para las secciones sin encabezado propio (ver TituloDeContinuacion).
        string? tituloBase = null;
        var saltarHasta = -1;
        for (var i = 0; i < hoja.Filas.Count; i++)
        {
            var fila = hoja.Filas[i];
            if (i <= saltarHasta) continue;

            if (i < indicePrimerEncabezado)
            {
                // Título de la primera sección: el texto de la fila inmediatamente anterior
                // al encabezado («LINEA B - ESTACION LACROZE»), salvo que sea la cabecera
                // del certificado. Más arriba hay datos del contrato que no son título.
                var t = fila[col.Item].Texto;
                if (fila.Numero == hoja.Filas[indicePrimerEncabezado].Numero - 1
                    && t.Length > 0 && t != encabezado && !TextoImport.PareceCodigo(t))
                    tituloPendiente = t;
                continue;
            }

            if (DetectarEncabezado(hoja, i) is Encabezado enc)
            {
                if (seccion is not null) CerrarSeccion(analisis, seccion, fila.Numero - 1);
                col = enc.Columnas;
                seccion = new SeccionCertificadoImport { Indice = analisis.Secciones.Count, FilaDesde = fila.Numero, Titulo = tituloPendiente ?? string.Empty };
                tituloBase = tituloPendiente;
                tituloPendiente = null;
                saltarHasta = i + enc.FilasOcupadas - 1;
                continue;
            }

            var textoItem = fila[col.Item].Texto;
            var esCodigo = TextoImport.PareceCodigo(textoItem);

            if (TextoDeTotal(fila, col) is string textoTotal)
            {
                if (seccion is not null)
                {
                    seccion.TotalMontoActualPlanilla = col.MontoActual is int cm ? fila[cm].Numero : null;
                    CerrarSeccion(analisis, seccion, fila.Numero);
                    seccion = null;
                }
                if (CierraTramo(textoTotal, tituloBase)) tituloBase = null;
                continue;
            }

            if (seccion is null)
            {
                // Entre secciones: un texto en la columna del código es candidato a título
                // (ej. «DEMASIAS»); un código abre una sección sin encabezado propio, con
                // las mismas columnas que la anterior. Entre varios textos gana el primero,
                // salvo que uno posterior diga demasías o economías y el primero no: es el
                // que decide a qué bloque se sugiere la sección.
                if (textoItem.Length == 0) continue;
                if (!esCodigo)
                {
                    var n = TextoImport.Normalizar(textoItem);
                    if (!n.StartsWith("s/") && !n.StartsWith("diferencia") && !n.StartsWith("certificado")
                        && (tituloPendiente is null || (!EsTituloDeTramo(tituloPendiente) && EsTituloDeTramo(textoItem))))
                        tituloPendiente = textoItem;
                    continue;
                }
                var (titulo, nuevaBase) = TituloDeContinuacion(tituloBase, tituloPendiente);
                seccion = new SeccionCertificadoImport { Indice = analisis.Secciones.Count, FilaDesde = fila.Numero, Titulo = titulo };
                tituloBase = nuevaBase;
                tituloPendiente = null;
            }

            if (!esCodigo) continue;

            // Renglón de ítem = fila con código y datos de contrato (unidad, cantidad o
            // subtotal). Un rubro codificado no los tiene, aunque arrastre ceros en las
            // columnas de porcentaje (Lacroze: «ELV.2.18 Varios» con 0 % y $ 0).
            var unidad = col.Unidad is int cu ? fila[cu].Texto : string.Empty;
            var esRenglon = unidad.Length > 0
                            || (col.Cantidad is int cc && fila[cc].Numero is not null)
                            || (col.Subtotal is int csu && fila[csu].Numero is not null);
            if (!esRenglon) continue;

            var pctActualCelda = fila[col.PctActual];
            var pctActual = LeerPorcentaje(pctActualCelda);
            var pctAnteriorCelda = col.PctAnterior is int ca ? fila[ca] : CeldaExcel.Vacia;

            seccion.Filas.Add(new FilaCertificadoImport
            {
                Fila = fila.Numero,
                Codigo = textoItem,
                Descripcion = fila[col.Descripcion].Texto,
                PctAnterior = LeerPorcentaje(pctAnteriorCelda),
                PctActual = pctActual,
                ActualIlegible = !pctActualCelda.EstaVacia && pctActual is null,
                MontoActualPlanilla = col.MontoActual is int cmo ? fila[cmo].Numero : null
            });
        }
        if (seccion is not null) CerrarSeccion(analisis, seccion, hoja.Filas[^1].Numero);

        if (analisis.Secciones.Count == 0)
            return new AnalisisCertificado { Encabezado = encabezado, Numero = numero, Mes = mes, Anio = anio, Error = "La hoja no tiene renglones de ítems debajo del encabezado." };
        return analisis;
    }

    /// <summary>
    /// Une varias secciones que van al mismo bloque (planillas con subtotales por rubro,
    /// que el parser corta como secciones): renglones concatenados y totales sumados. Si
    /// alguna no cerró con un total, la unión tampoco tiene total: una suma parcial daría
    /// una diferencia falsa contra lo calculado.
    /// </summary>
    public static SeccionCertificadoImport Combinar(IReadOnlyList<SeccionCertificadoImport> secciones)
    {
        if (secciones.Count == 1) return secciones[0];
        var union = new SeccionCertificadoImport
        {
            Indice = secciones[0].Indice,
            Titulo = string.Join(" + ", secciones.Select(s => s.Titulo)),
            FilaDesde = secciones.Min(s => s.FilaDesde),
            FilaHasta = secciones.Max(s => s.FilaHasta),
            TotalMontoActualPlanilla = secciones.All(s => s.TotalMontoActualPlanilla is not null)
                ? secciones.Sum(s => s.TotalMontoActualPlanilla!.Value)
                : null
        };
        foreach (var s in secciones) union.Filas.AddRange(s.Filas);
        return union;
    }

    /// <summary>
    /// Bloque sugerido para cada sección (índice → CertificadoEstructuraId; null = no importar):
    /// el que contiene al menos dos tercios de los códigos de la sección. Con empate o sin
    /// bloque que llegue, la elige el usuario. Los títulos solo cuidan el caso peligroso: una
    /// sección que dice demasías/BED/adicional no se sugiere en un bloque que no lo dice,
    /// porque las demasías repiten los códigos del básico (en Agüero, 21 de 40) y se aplicarían
    /// en silencio sobre él. Varias secciones pueden caer en el mismo bloque (una estación por
    /// sección, subtotales por rubro).
    /// </summary>
    public static Dictionary<int, int?> MapeoInicial(IReadOnlyList<SeccionCertificadoImport> secciones, IReadOnlyList<BloqueCertificadoVM> bloques)
    {
        var codigosPorBloque = bloques.ToDictionary(
            b => b.CertificadoEstructuraId,
            b => b.Items.Where(i => !i.EsAgrupador && !string.IsNullOrWhiteSpace(i.Codigo))
                .Select(i => TextoImport.ClaveCodigo(i.Codigo)).ToHashSet());

        var mapeo = new Dictionary<int, int?>();
        foreach (var s in secciones)
        {
            var seccionDeDemasias = HablaDeDemasias(s.Titulo);
            var candidatos = bloques
                .Where(b => !seccionDeDemasias || HablaDeDemasias(b.Titulo))
                .Select(b => (b.CertificadoEstructuraId,
                              Coinciden: s.Filas.Count(f => codigosPorBloque[b.CertificadoEstructuraId].Contains(TextoImport.ClaveCodigo(f.Codigo)))))
                .Where(c => c.Coinciden * 3 >= s.Filas.Count * 2)
                .OrderByDescending(c => c.Coinciden)
                .ToList();
            mapeo[s.Indice] = candidatos.Count == 1 || (candidatos.Count > 1 && candidatos[0].Coinciden > candidatos[1].Coinciden)
                ? candidatos[0].CertificadoEstructuraId
                : null;
        }
        return mapeo;
    }

    /// <summary>El título (de sección o de bloque) habla de demasías, BED o adicional.</summary>
    public static bool HablaDeDemasias(string titulo)
    {
        var n = TextoImport.Normalizar(titulo);
        return n.Contains("demas") || n.Contains("bed") || n.Contains("adicional");
    }

    /// <summary>
    /// Matchea los renglones de una sección contra los ítems hoja de un bloque, por
    /// código normalizado. Un código repetido dentro del bloque, o un renglón repetido
    /// para el mismo ítem, no se aplica (ambiguo); un % anterior distinto al del sistema
    /// se aplica igual pero se avisa.
    /// </summary>
    public static AplicacionBloque Aplicar(SeccionCertificadoImport seccion, BloqueCertificadoVM bloque)
    {
        var resultado = new AplicacionBloque
        {
            CertificadoEstructuraId = bloque.CertificadoEstructuraId,
            TituloBloque = bloque.Titulo,
            TotalMontoMesPlanilla = seccion.TotalMontoActualPlanilla
        };

        var hojas = bloque.Items.Where(i => !i.EsAgrupador).ToList();
        var porCodigo = hojas
            .Where(i => !string.IsNullOrWhiteSpace(i.Codigo))
            .ToLookup(i => TextoImport.ClaveCodigo(i.Codigo));
        var usados = new HashSet<int>();

        foreach (var fila in seccion.Filas)
        {
            if (fila.ActualIlegible)
            {
                // Texto no numérico en Actual: no se asume 0 (pisaría lo tipeado en la grilla).
                resultado.Detalle.Add(new DetalleFilaCertificado(fila, EstadoFilaCertificado.ValorIlegible, null));
                continue;
            }

            var candidatos = porCodigo[TextoImport.ClaveCodigo(fila.Codigo)].ToList();
            if (candidatos.Count == 0)
            {
                resultado.Detalle.Add(new DetalleFilaCertificado(fila, EstadoFilaCertificado.CodigoDesconocido, null));
                continue;
            }
            if (candidatos.Count > 1)
            {
                resultado.Detalle.Add(new DetalleFilaCertificado(fila, EstadoFilaCertificado.CodigoRepetido, null));
                continue;
            }

            var item = candidatos[0];
            if (!usados.Add(item.ItemEstructuraId))
            {
                // Segundo renglón para el mismo ítem (el mismo código en dos secciones unidas):
                // se conserva el primero y se avisa, en vez de pisarlo en silencio.
                resultado.Detalle.Add(new DetalleFilaCertificado(fila, EstadoFilaCertificado.CodigoRepetido, null));
                continue;
            }
            var pct = Math.Round(fila.PctActual ?? 0m, 4);
            resultado.Porcentajes[item.ItemEstructuraId] = pct;
            resultado.TotalMontoMesCalculado += CertificadoService.MovimientoDeMes(
                item.CantidadContrato, item.PUBasico, item.MontoContrato, pct).Monto;

            var anteriorSistema = item.PorcentajeAnterior ?? 0m;
            var estado = fila.PctAnterior is decimal pa && Math.Abs(pa - anteriorSistema) > ToleranciaPct
                ? EstadoFilaCertificado.AnteriorDistinto
                : EstadoFilaCertificado.Coincide;
            resultado.Detalle.Add(new DetalleFilaCertificado(fila, estado, anteriorSistema));
        }

        resultado.ItemsSinFila.AddRange(hojas.Where(i => !usados.Contains(i.ItemEstructuraId)));
        return resultado;
    }

    // ── helpers ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Título de una sección sin encabezado propio (la planilla sigue después de un
    /// total) y el título base con el que siguen las próximas. Sin título nuevo hereda
    /// el del tramo («DEMASIAS (cont.)»); dentro de un tramo de demasías, un título neutro
    /// queda como subtítulo («DEMASIAS › GENERALES DE PROYECTO»). Así la guarda de
    /// <see cref="MapeoInicial"/> cubre también esos tramos, que repiten códigos del
    /// básico. Un título de economías corta el tramo: se certifican sobre el básico. El
    /// total que nombra el tramo («TOTAL DEMASIAS») también lo corta (ver CierraTramo).
    /// </summary>
    private static (string Titulo, string? Base) TituloDeContinuacion(string? tituloBase, string? tituloPendiente)
    {
        if (tituloPendiente is null)
            return (tituloBase is null ? string.Empty : $"{tituloBase} (cont.)", tituloBase);
        if (tituloBase is not null && HablaDeDemasias(tituloBase) && !HablaDeDemasias(tituloPendiente)
            && !TextoImport.Normalizar(tituloPendiente).Contains("econom"))
            return ($"{tituloBase} › {tituloPendiente}", tituloBase);
        return (tituloPendiente, tituloPendiente);
    }

    /// <summary>
    /// true si la fila de total cierra el tramo del título base: el texto lo nombra
    /// («TOTAL DEMASIAS» con base «DEMASIAS») y no es un subtotal. Lo que siga sin título
    /// propio ya no hereda el tramo (unas economías no deben sugerirse en el bloque BED).
    /// </summary>
    private static bool CierraTramo(string textoTotal, string? tituloBase)
    {
        var tramo = TextoImport.Normalizar(tituloBase);
        var total = TextoImport.Normalizar(textoTotal);
        return tramo.Length > 0 && !total.StartsWith("sub") && total.Contains(tramo);
    }

    /// <summary>Título que decide el bloque sugerido: habla de demasías (o BED, adicional) o de economías.</summary>
    private static bool EsTituloDeTramo(string titulo) =>
        HablaDeDemasias(titulo) || TextoImport.Normalizar(titulo).Contains("econom");

    /// <summary>3 = coinciden N° y período; 2 = solo el N°; 1 = solo el período; 0 = nada.</summary>
    private static int Coincidencia(AnalisisCertificado a, int numero, int mes, int anio) =>
        (a.Numero == numero ? 2 : 0) + (a.Mes == mes && a.Anio == anio ? 1 : 0);

    private static bool MismoEncabezado(AnalisisCertificado a, AnalisisCertificado b) =>
        a.Numero == b.Numero && a.Mes == b.Mes && a.Anio == b.Anio;

    /// <summary>«CE B — N°15 jul/2026», «RESUMEN $ — sin renglones», «… · oculta».</summary>
    private static string Etiqueta(HojaExcel hoja, AnalisisCertificado? analisis) =>
        TextoImport.EtiquetaHoja(hoja, analisis switch
        {
            null => null,
            { Error: not null } => "sin renglones",
            { Numero: int n, Mes: int m, Anio: int a } => $"N°{n} {Meses[m - 1]}/{a}",
            { Numero: int n } => $"N°{n}",
            _ => "certificado"
        });

    /// <summary>
    /// Fracción con formato % → puntos; sin formato % se toma tal cual. Redondeo a 4. Null
    /// si la celda no es un número o si el % es absurdo (más de ±1000): se acota antes de
    /// multiplicar porque un 1E27 con formato % desborda el decimal.
    /// </summary>
    private static decimal? LeerPorcentaje(CeldaExcel celda)
    {
        if (celda.Numero is not decimal v) return null;
        var tope = celda.EsPorcentaje ? PctMaximoLegible / 100m : PctMaximoLegible;
        if (Math.Abs(v) > tope) return null;
        return Math.Round(celda.EsPorcentaje ? v * 100m : v, 4);
    }

    private static (int Indice, Encabezado Encabezado)? BuscarPrimerEncabezado(HojaExcel hoja)
    {
        for (var i = 0; i < Math.Min(hoja.Filas.Count, TextoImport.FilasDeBusquedaDeEncabezado); i++)
            if (DetectarEncabezado(hoja, i) is Encabezado enc) return (i, enc);
        return null;
    }

    /// <summary>
    /// Encabezado en la fila i: «Item» + «Descripción» y la columna «Actual». Anterior/
    /// Actual pueden estar en la misma fila (obra Premetro) o en la siguiente, debajo de
    /// «ACTA DE MEDICIÓN» (Agüero, Lacroze): se buscan en ambas.
    /// </summary>
    private static Encabezado? DetectarEncabezado(HojaExcel hoja, int i)
    {
        var fila = hoja.Filas[i];
        var t = TextoImport.Normalizadas(fila);
        if (!TextoImport.EsFilaDeEncabezado(t, out var item, out var descripcion)) return null;

        var filasOcupadas = 1;
        var (anterior, actual, montoActual) = ColumnasDePorcentaje(t, item);
        if (actual < 0)
        {
            if (TextoImport.FilaSiguiente(hoja, i) is not FilaExcel sub) return null;
            (anterior, actual, montoActual) = ColumnasDePorcentaje(TextoImport.Normalizadas(sub), item);
            if (actual < 0) return null;
            filasOcupadas = 2;
        }

        var unidad = Array.FindIndex(t, TextoImport.EsEncabezadoUnidad);
        var cantidad = Array.FindIndex(t, x => x.StartsWith("cant"));
        var subtotal = Array.FindIndex(t, x => x.Contains("subtotal"));
        var ultima = new[] { item, descripcion, unidad, cantidad, subtotal, anterior, actual, montoActual }.Max();
        return new Encabezado(
            new Columnas(item, descripcion, TextoImport.Opcional(unidad), TextoImport.Opcional(cantidad), TextoImport.Opcional(subtotal),
                TextoImport.Opcional(anterior), actual, TextoImport.Opcional(montoActual), ultima),
            filasOcupadas);
    }

    /// <summary>Primer «Anterior» y «Actual» (el acta, en %) y el segundo «Actual» (el certificado, en pesos).</summary>
    private static (int Anterior, int Actual, int MontoActual) ColumnasDePorcentaje(string[] t, int desde)
    {
        var anterior = TextoImport.Buscar(t, desde, x => x == "anterior");
        var actual = TextoImport.Buscar(t, desde, x => x == "actual");
        var montoActual = actual < 0 ? -1 : TextoImport.Buscar(t, actual + 1, x => x == "actual");
        return (anterior, actual, montoActual);
    }

    /// <summary>
    /// Texto de la fila «TOTAL …», o null si no es una fila de total: la palabra aparece en
    /// la columna del código o en alguna numérica (según la obra va en Item o en Precio
    /// Unitario, al principio o al final del texto), no es un código y la fila no tiene
    /// cantidad. La descripción no se mira.
    /// </summary>
    private static string? TextoDeTotal(FilaExcel fila, Columnas col)
    {
        if (col.Cantidad is int cc && fila[cc].Numero is not null) return null;
        for (var c = col.Item; c <= col.UltimaColumna; c++)
        {
            if (c == col.Descripcion) continue;
            var texto = fila[c].Texto;
            if (TextoImport.EsTextoDeTotal(texto)) return texto;
        }
        return null;
    }

    private static void CerrarSeccion(AnalisisCertificado analisis, SeccionCertificadoImport seccion, int filaHasta)
    {
        seccion.FilaHasta = filaHasta;
        if (seccion.Filas.Count == 0) return;
        if (seccion.Titulo.Length == 0) seccion.Titulo = $"Sección {analisis.Secciones.Count + 1}";
        analisis.Secciones.Add(seccion);
    }
}
