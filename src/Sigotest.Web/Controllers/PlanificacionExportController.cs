using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;
using SIGO.Data;
using SIGO.Models.Enums;
using SIGO.Services;

namespace SIGO.Controllers;

/// <summary>
/// Descarga a Excel de la planificación, en el layout tabular que consumen los
/// análisis de Presupuesto (las tablas que antes fabricaba Power Query sobre el
/// export de Flokzu): hoja "Contratos" (montos autorizados por bloque y moneda),
/// hoja "Planificacion" (curva: anticipo + mensuales + cola anual, formato largo)
/// y hoja "Contratos %" (matriz de % planificado por mes sobre el monto del contrato).
///
/// Modos: "vigente" (plan vivo, ciclo en curso), "corte" (la versión conocida por
/// Presupuesto a un mes dado: último snapshot ≤ anio/mes por obra) y "versiones"
/// (todas las versiones mensuales, el equivalente 1:1 del export completo de Flokzu).
/// </summary>
[Microsoft.AspNetCore.Authorization.Authorize(Roles = Roles.Planificacion)]
public class PlanificacionExportController(IDbContextFactory<AppDbContext> dbFactory) : Controller
{
    // GET: /PlanificacionExport/General?modo=vigente|corte|versiones&anio=&mes=&obras=1,2,3
    [HttpGet]
    public async Task<IActionResult> General(string? modo, int? anio, int? mes, string? obras)
    {
        var obraIds = ParseIds(obras);
        var filas = await FilasAsync(modo, anio, mes, obraIds, obraId: null);
        if (filas is null) return BadRequest("Modo de export inválido o corte incompleto.");

        var nombre = $"Planificacion_{(modo ?? "vigente").ToLowerInvariant()}{(anio.HasValue ? $"_{anio:0000}-{mes:00}" : "")}_{DateTime.Today:yyyyMMdd}.xlsx";
        return Excel(filas, nombre);
    }

    // GET: /PlanificacionExport/Obra/5?modo=vigente|corte|versiones&anio=&mes=
    [HttpGet]
    public async Task<IActionResult> Obra(int id, string? modo, int? anio, int? mes)
    {
        var filas = await FilasAsync(modo, anio, mes, idsFiltro: null, obraId: id);
        if (filas is null) return BadRequest("Modo de export inválido o corte incompleto.");
        if (filas.Count == 0) return NotFound();

        var nombre = $"Planificacion_Obra{id}_{(modo ?? "vigente").ToLowerInvariant()}_{DateTime.Today:yyyyMMdd}.xlsx";
        return Excel(filas, nombre);
    }

    // ── armado de filas ──────────────────────────────────────────────────────────

    /// <summary>Una celda de la planificación aplanada para el Excel (formato largo).</summary>
    private sealed record Fila(
        int NroPlanificacion, int AnioPlanificacion,
        string Licitacion, string Obra, string? Contratista, string? Director,
        DateTime? FechaInicio, DateTime? FechaPrevistaFin,
        string Estado, DateTime? FechaTomaConocimiento, string? TomadaPor,
        TipoAutorizante Tipo, int? Numero, string? Denominacion,
        ConceptoPlanMonto Concepto, int? MontoAnio, int? MontoMes, Moneda Moneda, decimal Monto);

    /// <summary>null = parámetros inválidos. El scope por rol (director solo sus obras) se aplica acá.</summary>
    private async Task<List<Fila>?> FilasAsync(string? modo, int? anio, int? mes, List<int>? idsFiltro, int? obraId)
    {
        modo = (modo ?? "vigente").ToLowerInvariant();
        if (modo == "corte" && (anio is null || mes is null)) return null;
        if (modo is not ("vigente" or "corte" or "versiones")) return null;

        await using var db = await dbFactory.CreateDbContextAsync();

        // Mismo scope que la lista: un director sin rol de alcance total solo exporta
        // sus obras asignadas.
        var soloDirector = Roles.SoloDirector(User.IsInRole);
        var wu = User.Identity?.Name;

        if (modo == "vigente")
        {
            // Una sola query a propósito (sin AsSplitQuery): el export debe ser una foto
            // consistente de plan+bloques+montos; partirla en round-trips permitiría leer
            // montos que ya no corresponden al plan leído.
            var query = db.Planificaciones.AsNoTracking()
                .Include(p => p.Obra).ThenInclude(o => o.DirectorUsuario)
                .Include(p => p.Autorizantes).ThenInclude(a => a.Montos)
                .AsQueryable();
            if (obraId is int oid) query = query.Where(p => p.ObraId == oid);
            if (idsFiltro is not null) query = query.Where(p => idsFiltro.Contains(p.ObraId));
            if (soloDirector) query = query.Where(p =>
                p.Obra.DirectorUsuario != null && p.Obra.DirectorUsuario.WindowsUser == wu);

            var hoy = DateTime.Today;
            return (await query.ToListAsync())
                .SelectMany(p => p.Autorizantes.SelectMany(a => a.Montos.Select(m => new Fila(
                    hoy.Month, hoy.Year,
                    p.Obra.NumeroLicitacion, p.Obra.Nombre, p.Obra.Contratista, p.Obra.DirectorNombre,
                    p.Obra.FechaActaInicio ?? p.Obra.FechaContrato, p.Obra.FechaFinalContrato,
                    p.Estado.ToString(), p.FechaTomaConocimiento, p.TomadaConocimientoPor,
                    a.Tipo, a.Numero, a.Denominacion,
                    m.Concepto, m.Anio, m.Mes, m.Moneda, m.Monto))))
                .ToList();
        }

        var snaps = db.PlanificacionSnapshots.AsNoTracking()
            .Include(s => s.Obra).ThenInclude(o => o.DirectorUsuario)
            .Include(s => s.Montos)
            .AsQueryable();
        if (obraId is int oid2) snaps = snaps.Where(s => s.ObraId == oid2);
        if (idsFiltro is not null) snaps = snaps.Where(s => idsFiltro.Contains(s.ObraId));
        if (soloDirector) snaps = snaps.Where(s =>
            s.Obra.DirectorUsuario != null && s.Obra.DirectorUsuario.WindowsUser == wu);

        if (modo == "corte")
        {
            // As-of: para cada obra, su última versión hasta el corte inclusive.
            snaps = snaps.Where(s => s.Anio < anio || (s.Anio == anio && s.Mes <= mes));
            var ultimos = await snaps
                .GroupBy(s => s.ObraId)
                .Select(g => g.OrderByDescending(s => s.Anio).ThenByDescending(s => s.Mes).First().Id)
                .ToListAsync();
            snaps = db.PlanificacionSnapshots.AsNoTracking()
                .Include(s => s.Obra).ThenInclude(o => o.DirectorUsuario)
                .Include(s => s.Montos)
                .Where(s => ultimos.Contains(s.Id));
        }

        return (await snaps.ToListAsync())
            .SelectMany(s => s.Montos.Select(m => new Fila(
                s.Mes, s.Anio,
                s.Obra.NumeroLicitacion, s.Obra.Nombre, s.Obra.Contratista, s.Obra.DirectorNombre,
                s.Obra.FechaActaInicio ?? s.Obra.FechaContrato, s.Obra.FechaFinalContrato,
                nameof(EstadoPlanificacion.Aprobada), s.FechaTomaConocimiento, s.TomadaConocimientoPor,
                m.Tipo, m.Numero, m.Denominacion,
                m.Concepto, m.Anio, m.Mes, m.Moneda, m.Monto)))
            .ToList();
    }

    // ── Excel ────────────────────────────────────────────────────────────────────

    private FileContentResult Excel(List<Fila> filas, string nombreArchivo)
    {
        using var wb = new XLWorkbook();

        // Hoja 1 — Contratos: una fila por versión × bloque × moneda con el monto
        // autorizado. Se parte de los bloques (no de las filas MontoAutorizado): un
        // bloque con curva pero sin monto autorizado cargado igual debe listarse,
        // con importe 0 — si no, la hoja queda vacía hasta que alguien cargue el dato.
        var contratos = filas
            .GroupBy(f => (f.NroPlanificacion, f.AnioPlanificacion, f.Licitacion, f.Obra, f.Tipo, f.Numero))
            .SelectMany(g => g.Select(f => f.Moneda).Distinct().Select(mon =>
            {
                var autorizado = g.FirstOrDefault(f =>
                    f.Concepto == ConceptoPlanMonto.MontoAutorizado && f.Moneda == mon);
                return (autorizado ?? g.First(f => f.Moneda == mon)) with
                {
                    Concepto = ConceptoPlanMonto.MontoAutorizado,
                    MontoAnio = null,
                    MontoMes = null,
                    Moneda = mon,
                    Monto = autorizado?.Monto ?? 0m
                };
            }))
            .ToList();
        EscribirHoja(wb.Worksheets.Add("Contratos"), contratos, conMes: false);

        // Hoja 2 — Planificacion: la curva en formato largo (anticipo sin mes, mensuales
        // con su mes, cola anual como enero del año — mismo criterio que el circuito viejo).
        EscribirHoja(wb.Worksheets.Add("Planificacion"),
            filas.Where(f => f.Concepto is ConceptoPlanMonto.AnticipoFinanciero
                or ConceptoPlanMonto.Mensual or ConceptoPlanMonto.CalculoAnual),
            conMes: true);

        // Hoja 3 — Contratos %: la matriz del "Contratos %.xlsx" viejo, ya resuelta
        // (sin INDEX/MATCH): una fila por versión × bloque × moneda y una columna por
        // mes con importe planificado / monto del contrato.
        EscribirHojaPorcentajes(wb.Worksheets.Add("Contratos %"), contratos, filas);

        return File(ExcelHelpers.ToBytes(wb), ExcelHelpers.ContentType, nombreArchivo);
    }

    /// <summary>
    /// Matriz de porcentajes: cada celda es el monto del período dividido por el monto
    /// del contrato del bloque (misma cuenta que hacían las fórmulas de "Contratos %").
    /// Sin monto de contrato cargado (o sin monto en el período) la celda queda vacía.
    /// </summary>
    private static void EscribirHojaPorcentajes(IXLWorksheet ws, List<Fila> contratos, List<Fila> filas)
    {
        var meses = filas
            .Where(f => f.Concepto == ConceptoPlanMonto.Mensual && f.MontoAnio.HasValue && f.MontoMes.HasValue)
            .Select(f => (Anio: f.MontoAnio!.Value, Mes: f.MontoMes!.Value))
            .Distinct().OrderBy(m => m.Anio).ThenBy(m => m.Mes).ToList();
        var anios = filas
            .Where(f => f.Concepto == ConceptoPlanMonto.CalculoAnual && f.MontoAnio.HasValue)
            .Select(f => f.MontoAnio!.Value)
            .Distinct().OrderBy(a => a).ToList();

        var porBloque = filas
            .GroupBy(f => (f.NroPlanificacion, f.AnioPlanificacion, f.Licitacion, f.Obra, f.Tipo, f.Numero, f.Moneda))
            .ToDictionary(g => g.Key, g => g.ToList());

        string[] fijas =
        [
            "Nro planificacion", "Año", "Licitación", "Obra", "Contratista",
            "Concepto", "Numero", "Moneda", "Monto contrato", "Anticipo"
        ];
        var col = 1;
        foreach (var h in fijas) ws.Cell(1, col++).Value = h;
        foreach (var (a, m) in meses)
        {
            ws.Cell(1, col).Value = new DateTime(a, m, 1);
            ws.Cell(1, col++).Style.DateFormat.Format = "mmm-yy";
        }
        foreach (var a in anios) ws.Cell(1, col++).Value = $"Año {a}";
        ws.Cell(1, col).Value = "Total planificado";
        ws.Range(1, 1, 1, col).Style.Font.Bold = true;
        ws.Range(1, 1, 1, col).Style.Fill.BackgroundColor = XLColor.LightGray;

        var r = 2;
        foreach (var f in contratos.OrderBy(f => f.Licitacion).ThenBy(f => f.AnioPlanificacion)
                     .ThenBy(f => f.NroPlanificacion).ThenBy(f => f.Tipo).ThenBy(f => f.Numero).ThenBy(f => f.Moneda))
        {
            porBloque.TryGetValue(
                (f.NroPlanificacion, f.AnioPlanificacion, f.Licitacion, f.Obra, f.Tipo, f.Numero, f.Moneda),
                out var montos);
            decimal Suma(Func<Fila, bool> pred) => montos?.Where(pred).Sum(m => m.Monto) ?? 0m;

            var c = 1;
            ws.Cell(r, c++).Value = f.NroPlanificacion;
            ws.Cell(r, c++).Value = f.AnioPlanificacion;
            ws.Cell(r, c++).Value = f.Licitacion;
            ws.Cell(r, c++).Value = f.Obra;
            ws.Cell(r, c++).Value = f.Contratista;
            ws.Cell(r, c++).Value = ConceptoTexto(f);
            if (f.Numero is int n) ws.Cell(r, c).Value = n;
            c++;
            ws.Cell(r, c++).Value = f.Moneda.ToString();
            ws.Cell(r, c).Value = f.Monto;
            ws.Cell(r, c++).Style.NumberFormat.Format = ExcelHelpers.FormatoMoneda;

            var contrato = f.Monto;
            void Pct(int celda, decimal valor)
            {
                if (contrato == 0 || valor == 0) return; // misma regla que el Excel viejo: en 0 queda vacío
                ws.Cell(r, celda).Value = valor / contrato;
                ws.Cell(r, celda).Style.NumberFormat.Format = "0.00%";
            }

            Pct(c++, Suma(m => m.Concepto == ConceptoPlanMonto.AnticipoFinanciero));
            foreach (var (a, mm) in meses)
                Pct(c++, Suma(m => m.Concepto == ConceptoPlanMonto.Mensual && m.MontoAnio == a && m.MontoMes == mm));
            foreach (var a in anios)
                Pct(c++, Suma(m => m.Concepto == ConceptoPlanMonto.CalculoAnual && m.MontoAnio == a));
            Pct(c, Suma(m => m.Concepto is ConceptoPlanMonto.AnticipoFinanciero
                or ConceptoPlanMonto.Mensual or ConceptoPlanMonto.CalculoAnual));
            r++;
        }

        // Solo las columnas de identificación: autoajustar 120+ columnas de meses es lento
        // y el ancho fijo les queda bien.
        ws.Columns(1, fijas.Length).AdjustToContents(1, Math.Min(r, 50));
    }

    private static void EscribirHoja(IXLWorksheet ws, IEnumerable<Fila> filas, bool conMes)
    {
        string[] headers =
        [
            "Nro planificacion", "Año", "Licitación", "Obra", "Contratista", "Director de obra",
            "Estado", "Fecha de inicio", "Fecha prevista fin", "Fecha toma conocimiento", "Tomada por",
            "Concepto", "Numero", "Denominación", "Importe", "Moneda",
            .. conMes ? new[] { "Mes" } : Array.Empty<string>()
        ];
        for (int c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
        ws.Range(1, 1, 1, headers.Length).Style.Font.Bold = true;
        ws.Range(1, 1, 1, headers.Length).Style.Fill.BackgroundColor = XLColor.LightGray;

        var r = 2;
        foreach (var f in filas.OrderBy(f => f.Licitacion).ThenBy(f => f.AnioPlanificacion)
                     .ThenBy(f => f.NroPlanificacion).ThenBy(f => f.Tipo).ThenBy(f => f.Numero)
                     .ThenBy(f => f.Moneda).ThenBy(f => f.MontoAnio).ThenBy(f => f.MontoMes))
        {
            var c = 1;
            ws.Cell(r, c++).Value = f.NroPlanificacion;
            ws.Cell(r, c++).Value = f.AnioPlanificacion;
            ws.Cell(r, c++).Value = f.Licitacion;
            ws.Cell(r, c++).Value = f.Obra;
            ws.Cell(r, c++).Value = f.Contratista;
            ws.Cell(r, c++).Value = f.Director;
            ws.Cell(r, c++).Value = f.Estado;
            Fecha(ws.Cell(r, c++), f.FechaInicio);
            Fecha(ws.Cell(r, c++), f.FechaPrevistaFin);
            Fecha(ws.Cell(r, c++), f.FechaTomaConocimiento);
            ws.Cell(r, c++).Value = f.TomadaPor;
            ws.Cell(r, c++).Value = ConceptoTexto(f);
            if (f.Numero is int n) ws.Cell(r, c).Value = n;
            c++;
            ws.Cell(r, c++).Value = f.Denominacion;
            ws.Cell(r, c).Value = f.Monto;
            ws.Cell(r, c++).Style.NumberFormat.Format = ExcelHelpers.FormatoMoneda;
            ws.Cell(r, c++).Value = f.Moneda.ToString();
            if (conMes)
                Fecha(ws.Cell(r, c), f switch
                {
                    { Concepto: ConceptoPlanMonto.Mensual } => new DateTime(f.MontoAnio!.Value, f.MontoMes!.Value, 1),
                    { Concepto: ConceptoPlanMonto.CalculoAnual } => new DateTime(f.MontoAnio!.Value, 1, 1),
                    // Anticipo: con su mes de pago si lo tiene asignado; vacío si no.
                    { Concepto: ConceptoPlanMonto.AnticipoFinanciero, MontoAnio: int a, MontoMes: int m } => new DateTime(a, m, 1),
                    _ => (DateTime?)null
                });
            r++;
        }

        ws.Columns().AdjustToContents(1, Math.Min(r, 50));
    }

    private static void Fecha(IXLCell celda, DateTime? valor)
    {
        if (valor is null) return;
        celda.Value = valor.Value;
        celda.Style.DateFormat.Format = "dd/MM/yyyy";
    }

    /// <summary>Vocabulario del export viejo: "Obra Base" / "Adicional" / "BED" (+ prefijo Anticipo).</summary>
    private static string ConceptoTexto(Fila f)
    {
        var bloque = f.Tipo switch
        {
            TipoAutorizante.Basica => "Obra Base",
            TipoAutorizante.Adicional => "Adicional",
            TipoAutorizante.BED => "BED",
            _ => f.Tipo.ToString()
        };
        return f.Concepto == ConceptoPlanMonto.AnticipoFinanciero ? $"Anticipo {bloque}" : bloque;
    }

    private static List<int>? ParseIds(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return null;
        var ids = new List<int>();
        foreach (var parte in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (int.TryParse(parte, out var id)) ids.Add(id);
        return ids.Count > 0 ? ids : null;
    }
}
