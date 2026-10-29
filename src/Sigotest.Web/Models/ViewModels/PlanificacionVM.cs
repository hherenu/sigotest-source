using SIGO.Models.Enums;

namespace SIGO.Models.ViewModels;

/// <summary>
/// VM de la grilla de planificación de una obra: bloques (Autorizantes) × períodos × monedas.
/// El horizonte de períodos se deriva de las fechas de la obra (meses) con buckets anuales
/// para la cola. Los subtotales y totales se calculan en la página sobre los valores en edición.
/// </summary>
public class PlanificacionVM
{
    // Datos aplanados (sin entidades EF): ver CertificadoVM.
    public int PlanificacionId { get; set; }
    public EstadoPlanificacion Estado { get; set; }
    public DateTime? FechaTomaConocimiento { get; set; }
    public string? MotivoRevision { get; set; }
    public string? Correcciones { get; set; }
    public bool ObraFinalizada { get; set; }
    public int ObraId { get; set; }
    public string ObraNombre { get; set; } = string.Empty;
    public string NumeroLicitacion { get; set; } = string.Empty;

    /// <summary>Presupuestos de la obra por moneda (oficial/adjudicado) con la regla de cuál aplica.</summary>
    public PresupuestosObra Presupuestos { get; set; } = new(0m, null, null, null, null, null);

    /// <summary>
    /// Mes de inicio de la obra (acta de inicio o, si no hay, fecha de contrato) contra el
    /// que se compara el mes del anticipo, y cómo se nombra ese dato en los mensajes
    /// (<see cref="InicioEtiqueta"/>: "mes del acta de inicio" / "mes de la fecha de
    /// contrato"). Ambos null si la obra no tiene ninguna de las dos fechas: ahí el
    /// horizonte arranca en el mes actual y el servicio NO exige esa regla (ver
    /// PlanificacionService.EtiquetaInicio); la página evalúa con estos mismos valores
    /// para decir exactamente lo que el servicio va a exigir.
    /// </summary>
    public PeriodoVM? Inicio { get; set; }
    public string? InicioEtiqueta { get; set; }

    /// <summary>
    /// Autorizado de la Obra Básica en la moneda: el presupuesto que aplica (regla única
    /// en <see cref="PresupuestosObra"/>). También es la base del tope del 50% de adicionales/BED.
    /// </summary>
    public decimal PresupuestoReferencia(Moneda moneda) => Presupuestos.Referencia(moneda);

    /// <summary>"Presupuesto adjudicado" u "oficial", según cuál aplique.</summary>
    public string PresupuestoEtiqueta => Presupuestos.Etiqueta;

    /// <summary>Token de concurrencia de la sesión de edición (viaja a GuardarGrillaAsync).</summary>
    public byte[] RowVersion { get; set; } = [];

    /// <summary>Monedas mostradas en la grilla.</summary>
    public List<Moneda> Monedas { get; set; } = [Moneda.Pesos, Moneda.USD, Moneda.EUR];

    /// <summary>Horizonte: meses del plazo + buckets anuales para la cola.</summary>
    public List<PeriodoVM> Periodos { get; set; } = [];

    public List<BloqueAutorizanteVM> Bloques { get; set; } = [];
}

/// <summary>Un período del horizonte: mensual (Anio+Mes) o anual (solo Anio).</summary>
public record PeriodoVM(int Anio, int? Mes)
{
    public bool EsAnual => Mes is null;
}

public class BloqueAutorizanteVM
{
    public int AutorizanteId { get; set; }
    public TipoAutorizante Tipo { get; set; }
    public int? Numero { get; set; }
    public string? Denominacion { get; set; }
    public string Titulo { get; set; } = string.Empty;

    /// <summary>
    /// Lookup de montos EFECTIVOS del bloque: (Concepto, Anio, Mes, Moneda) → Monto
    /// (ver PlanificacionService.MontosEfectivos). Para la Obra Básica, las claves
    /// (MontoAutorizado, null, null, moneda) son el presupuesto de la obra por moneda
    /// —no filas editables ni persistidas en el plan—.
    /// </summary>
    public Dictionary<(ConceptoPlanMonto Concepto, int? Anio, int? Mes, Moneda Moneda), decimal> Valores { get; set; } = [];

    /// <summary>Monto cargado para una celda (0 si no existe).</summary>
    public decimal Valor(ConceptoPlanMonto concepto, int? anio, int? mes, Moneda moneda) =>
        Valores.TryGetValue((concepto, anio, mes, moneda), out var v) ? v : 0m;

    /// <summary>
    /// Detalle de la curva GUARDADA (Mensual/CalculoAnual) que cae FUERA del conjunto de
    /// períodos renderizados por la grilla, en orden cronológico. GuardarGrillaAsync
    /// preserva estos montos (obra larga / fin de contrato adelantado) y el export y el
    /// snapshot los incluyen; la grilla los suma en los totales y los muestra en un aviso
    /// porque no tienen celda editable. ÚNICA implementación del filtro: la suma
    /// (<see cref="CurvaFueraDeHorizonte"/>) delega acá.
    /// <paramref name="periodosRender"/> = claves (Concepto, Anio, Mes) de los períodos
    /// que la grilla sí edita.
    /// </summary>
    public List<(int? Anio, int? Mes, Moneda Moneda, decimal Monto)> DetalleFueraDeHorizonte(
        IReadOnlySet<(ConceptoPlanMonto Concepto, int? Anio, int? Mes)> periodosRender) =>
        Valores.Where(kv => kv.Key.Concepto is ConceptoPlanMonto.Mensual or ConceptoPlanMonto.CalculoAnual
                && !periodosRender.Contains((kv.Key.Concepto, kv.Key.Anio, kv.Key.Mes)))
            .Select(kv => (kv.Key.Anio, kv.Key.Mes, kv.Key.Moneda, kv.Value))
            .OrderBy(f => f.Anio).ThenBy(f => f.Mes).ThenBy(f => f.Moneda)
            .ToList();

    /// <summary>Suma por moneda de <see cref="DetalleFueraDeHorizonte"/> (los totales de la grilla).</summary>
    public decimal CurvaFueraDeHorizonte(Moneda moneda,
        IReadOnlySet<(ConceptoPlanMonto Concepto, int? Anio, int? Mes)> periodosRender) =>
        DetalleFueraDeHorizonte(periodosRender).Where(f => f.Moneda == moneda).Sum(f => f.Monto);

    /// <summary>
    /// Anticipo del bloque para una moneda, tenga o no mes asignado (la grilla edita
    /// el importe en una sola fila; el mes es un atributo aparte del bloque).
    /// </summary>
    public decimal ValorAnticipo(Moneda moneda) =>
        Valores.Where(kv => kv.Key.Concepto == ConceptoPlanMonto.AnticipoFinanciero && kv.Key.Moneda == moneda)
            .Sum(kv => kv.Value);

    /// <summary>Mes asignado al anticipo del bloque (null = sin fecha, datos anteriores a la mejora).</summary>
    public PeriodoVM? MesAnticipo()
    {
        // El mes es un atributo del bloque, pero se guarda por-fila (por moneda). Se ordena
        // para que, ante datos con meses divergentes por moneda, el resultado sea determinístico.
        var k = Valores.Keys
            .Where(k => k.Concepto == ConceptoPlanMonto.AnticipoFinanciero && k.Anio.HasValue && k.Mes.HasValue)
            .OrderBy(k => k.Anio).ThenBy(k => k.Mes)
            .FirstOrDefault();
        return k.Anio.HasValue ? new PeriodoVM(k.Anio.Value, k.Mes) : null;
    }
}

/// <summary>
/// Agregados EN VIVO de la grilla de planificación, puros y testeables: reciben las
/// celdas de la sesión de edición (lo tipeado, guardado o no) más los bloques y
/// períodos del VM, y calculan los totales del pie de la grilla. "Planificado" =
/// anticipo + períodos renderizados en vivo + curva guardada FUERA del horizonte
/// (GuardarGrillaAsync la preserva y el export/snapshot la incluyen: mirar solo los
/// períodos renderizados subestimaría lo que realmente queda registrado).
/// </summary>
public class TotalesPlan(
    IReadOnlyDictionary<(int AutorizanteId, ConceptoPlanMonto Concepto, int? Anio, int? Mes, Moneda Moneda), decimal?> celdas,
    IReadOnlyList<BloqueAutorizanteVM> bloques,
    IReadOnlyList<PeriodoVM> periodos)
{
    // Claves de los períodos que la grilla renderiza (y por lo tanto ya suma en vivo).
    // Se arma UNA vez por instancia: la página consulta los totales decenas de veces
    // por render y antes cada llamada reconstruía este conjunto.
    private readonly HashSet<(ConceptoPlanMonto Concepto, int? Anio, int? Mes)> render =
        [.. periodos.Select(p => (p.EsAnual ? ConceptoPlanMonto.CalculoAnual : ConceptoPlanMonto.Mensual, (int?)p.Anio, p.Mes))];

    /// <summary>Valor en edición de una celda (0 si no existe o está vacía).</summary>
    public decimal Valor(int autorizanteId, ConceptoPlanMonto concepto, int? anio, int? mes, Moneda moneda) =>
        celdas.TryGetValue((autorizanteId, concepto, anio, mes, moneda), out var v) ? v.GetValueOrDefault() : 0m;

    public decimal SumaPlanificado(int autorizanteId, Moneda moneda)
    {
        var enVivo = Valor(autorizanteId, ConceptoPlanMonto.AnticipoFinanciero, null, null, moneda)
            + periodos.Sum(p => Valor(autorizanteId,
                p.EsAnual ? ConceptoPlanMonto.CalculoAnual : ConceptoPlanMonto.Mensual, p.Anio, p.Mes, moneda));

        var bloque = bloques.FirstOrDefault(b => b.AutorizanteId == autorizanteId);
        return enVivo + (bloque?.CurvaFueraDeHorizonte(moneda, render) ?? 0m);
    }

    /// <summary>
    /// Autorizado del bloque. La Obra Básica no tiene celda editable: su autorizado es el
    /// presupuesto de la obra, que viene en los Valores del bloque (no en `celdas`).
    /// </summary>
    public decimal MontoAutorizado(int autorizanteId, Moneda moneda)
    {
        var bloque = bloques.FirstOrDefault(b => b.AutorizanteId == autorizanteId);
        return bloque?.Tipo == TipoAutorizante.Basica
            ? bloque.Valor(ConceptoPlanMonto.MontoAutorizado, null, null, moneda)
            : Valor(autorizanteId, ConceptoPlanMonto.MontoAutorizado, null, null, moneda);
    }

    public decimal AutorizadoVsPlanificado(int autorizanteId, Moneda moneda) =>
        MontoAutorizado(autorizanteId, moneda) - SumaPlanificado(autorizanteId, moneda);

    public decimal PlanificadoPorTipo(TipoAutorizante tipo, Moneda moneda) =>
        bloques.Where(b => b.Tipo == tipo).Sum(b => SumaPlanificado(b.AutorizanteId, moneda));

    public decimal TotalPlanificado(Moneda moneda) =>
        bloques.Sum(b => SumaPlanificado(b.AutorizanteId, moneda));

    public decimal TotalAutorizado(Moneda moneda) =>
        bloques.Sum(b => MontoAutorizado(b.AutorizanteId, moneda));

    /// <summary>
    /// Curva guardada fuera del horizonte de un bloque (todas las monedas), para el
    /// aviso de la grilla: montos que suman en los totales pero no tienen celda visible.
    /// </summary>
    public List<(int? Anio, int? Mes, Moneda Moneda, decimal Monto)> FueraDeHorizonte(int autorizanteId) =>
        bloques.FirstOrDefault(b => b.AutorizanteId == autorizanteId)?.DetalleFueraDeHorizonte(render) ?? [];
}

/// <summary>
/// Qué se conserva de lo tipeado en la sesión cuando la página relee el plan de la base
/// (agregar o quitar un bloque, una transición o un guardado fallido). Fusión de tres vías
/// por clave: se conserva lo que la sesión cambió respecto de lo guardado ANTES y cuya
/// clave nadie cambió en la base desde entonces; si otro usuario la cambió, manda la base
/// y la clave cuenta como pisada para avisarlo. Conservar todo lo de la sesión llevaba
/// valores viejos de celdas no tocadas al token nuevo, y un segundo Guardar tras un
/// conflicto pisaba en silencio lo que el otro usuario había guardado.
/// </summary>
public static class FusionSesion
{
    public static (Dictionary<TClave, TValor> Conservar, int Pisadas) Fusionar<TClave, TValor>(
        IEnumerable<KeyValuePair<TClave, TValor>> sesion,
        Func<TClave, TValor> guardadoAntes,
        Func<TClave, TValor> guardadoAhora) where TClave : notnull
    {
        var igual = EqualityComparer<TValor>.Default;
        var conservar = new Dictionary<TClave, TValor>();
        var pisadas = 0;
        foreach (var (clave, valor) in sesion)
        {
            var antes = guardadoAntes(clave);
            if (igual.Equals(valor, antes)) continue;          // la sesión no la cambió
            var ahora = guardadoAhora(clave);
            if (igual.Equals(ahora, antes)) conservar[clave] = valor;
            else if (!igual.Equals(ahora, valor)) pisadas++;   // la cambió otro: manda la base
        }
        return (conservar, pisadas);
    }
}

/// <summary>
/// Monto "efectivo" de un bloque: lo persistido, salvo que para la Obra Básica el
/// MontoAutorizado es el presupuesto de la obra (ver PlanificacionService.MontosEfectivos).
/// Lo consumen balance, VM, snapshot y export para no repetir esa regla.
/// </summary>
public record MontoEfectivoVM(ConceptoPlanMonto Concepto, int? Anio, int? Mes, Moneda Moneda, decimal Monto);

/// <summary>Fila de entrada al guardar la grilla (una celda editada).</summary>
public record PlanMontoInput(
    int AutorizanteId,
    ConceptoPlanMonto Concepto,
    int? Anio,
    int? Mes,
    Moneda Moneda,
    decimal Monto);

// ── Listado / lecturas ──────────────────────────────────────────────────────────────

/// <summary>
/// Resultado del listado de planificación: filas (obras con el estado de su plan),
/// los cortes mensuales disponibles y el alcance con el que se armó.
/// </summary>
public class PlanListadoVM
{
    /// <summary>true si el usuario solo ve sus obras asignadas como director.</summary>
    public bool SoloDirector { get; set; }

    /// <summary>Obras visibles, ordenadas por nombre.</summary>
    public List<PlanObraRowVM> Obras { get; set; } = [];

    /// <summary>Meses con al menos una versión tomada (más reciente primero), para el selector de corte.</summary>
    public List<CorteMesVM> Cortes { get; set; } = [];
}

/// <summary>Fila del listado: la obra más el estado de su plan (aplanado, sin entidades).</summary>
public class PlanObraRowVM
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string NumeroLicitacion { get; set; } = string.Empty;
    public string? DirectorObra { get; set; }

    /// <summary>Estado del plan de la obra (null = todavía sin plan: "Sin iniciar").</summary>
    public EstadoPlanificacion? EstadoPlan { get; set; }
    public DateTime? FechaTomaConocimiento { get; set; }
    public bool ObraFinalizada { get; set; }

    /// <summary>Columna «Estado del plan»: lo que se ve y lo que ofrece su filtro.</summary>
    public string EstadoPlanTexto => EstadoPlan is EstadoPlanificacion e ? Components.Shared.EstadoUi.Texto(e) : "Sin iniciar";

    /// <summary>Columna «Ciclo del mes» mientras el plan no está aprobado ni la obra finalizada.</summary>
    public const string SinCiclo = "—";

    /// <summary>Columna «Ciclo del mes»: lo que se ve y lo que ofrece su filtro.</summary>
    public string CicloDelMes =>
        ObraFinalizada ? "Obra finalizada"
        : FechaTomaConocimiento is DateTime ft ? $"Conocida el {ft.ToLocalTime():dd/MM}"
        : EstadoPlan == EstadoPlanificacion.Aprobada ? "Falta toma de conocimiento"
        : SinCiclo;

    /// <summary>
    /// Columna «Versión al corte»: depende del corte elegido y no de la obra, así que la
    /// completa el listado al elegirlo con <see cref="TextoVersionAlCorte"/> (texto de la
    /// columna y de su filtro).
    /// </summary>
    public string? VersionAlCorte { get; set; }

    /// <summary>«Sep 2026 — conocida el 05/10/2026», o «Sin versión» si la obra no tiene una al corte.</summary>
    public static string TextoVersionAlCorte(VersionCorteVM? version) =>
        version is null
            ? "Sin versión"
            : $"{Components.Shared.Fmt.MesCorto(version.Mes)} {version.Anio} — conocida el {version.FechaTomaConocimiento.ToLocalTime():dd/MM/yyyy}";
}

/// <summary>Un mes con versiones tomadas, ofrecido como corte del listado.</summary>
public record CorteMesVM(int Anio, int Mes);

/// <summary>Versión vigente de una obra a un corte (la última toma hasta ese mes inclusive).</summary>
public record VersionCorteVM(int Anio, int Mes, DateTime FechaTomaConocimiento);

/// <summary>Una versión mensual (snapshot por toma de conocimiento) del plan de una obra.</summary>
public record PlanVersionVM(int Anio, int Mes, DateTime FechaTomaConocimiento, string? TomadaConocimientoPor);

/// <summary>
/// Veredicto de acceso a la planificación de una obra, en el orden en que la UI lo
/// muestra: primero la existencia y recién después la asignación al director.
/// </summary>
public enum AccesoPlanObra
{
    Ok,

    /// <summary>La obra no existe.</summary>
    NoEncontrada,

    /// <summary>La obra está "Proyectada" (sin plazo ni expediente): todavía no se planifica.</summary>
    Proyectada,

    /// <summary>El usuario es solo-director y la obra no le está asignada.</summary>
    NoAsignada
}
