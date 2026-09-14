using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SIGO.Data;
using SIGO.Models;
using SIGO.Models.Enums;
using SIGO.Services.Validaciones;

namespace SIGO.Services;

/// <summary>
/// Rollover mensual del ciclo de planificación: el día configurado (Correo:DiaRollover,
/// default 9 — la víspera del digest) devuelve a Pendiente todos los planes con el
/// circuito iniciado (Cargada / En revisión / Aprobada, con o sin toma de conocimiento)
/// salvo los de obras finalizadas, para que el mes nuevo vuelva a recorrer carga →
/// aprobación → toma de conocimiento (y con ella el snapshot del mes). La grilla NO se
/// toca: el director arranca con los montos del mes anterior precargados y solo
/// corrige lo que cambió antes de volver a marcar Cargada. No envía mails: el aviso es
/// el digest del día siguiente. Si la app estuvo apagada ese día, corre al arrancar
/// (catch-up dentro del mes); el registro en RolloversPlanificacionEjecutados evita
/// repetirlo por reinicios.
/// </summary>
public class PlanificacionRolloverService(
    IServiceProvider services,
    IOptionsMonitor<CorreoOptions> opciones,
    ILogger<PlanificacionRolloverService> logger) : TareaMensualHostedService(logger)
{
    protected override string Descripcion => "el rollover de planificación";

    protected override async Task ChequearAsync(CancellationToken ct)
    {
        var hoy = DateTime.Today;
        if (hoy.Day < opciones.CurrentValue.DiaRollover) return;

        // BackgroundService es singleton: los servicios scoped se resuelven por corrida.
        using var scope = services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var reiniciados = await EjecutarAsync(db, hoy, ct);
        if (reiniciados is null) return; // el rollover de este mes ya corrió

        logger.LogInformation("Rollover de planificación {Mes:00}/{Anio}: {Planes} plan(es) reiniciado(s) " +
            "para el ciclo nuevo.", hoy.Month, hoy.Year, reiniciados);
    }

    /// <summary>
    /// Corre el rollover del mes de <paramref name="hoy"/> si aún no corrió: devuelve a
    /// Pendiente (limpiando fechas de carga/aprobación, motivo de revisión y toma de
    /// conocimiento) los planes con circuito iniciado de obras NO finalizadas; los
    /// montos de la grilla y los snapshots quedan intactos. Devuelve la cantidad de
    /// planes reiniciados, o null si el mes ya estaba registrado (idempotencia).
    /// Separado del hosted service para poder testearlo contra la DB real.
    /// </summary>
    public static async Task<int?> EjecutarAsync(AppDbContext db, DateTime hoy, CancellationToken ct = default)
    {
        if (await db.RolloversPlanificacionEjecutados.AnyAsync(r => r.Anio == hoy.Year && r.Mes == hoy.Month, ct))
            return null;

        // Set-based: no hay lógica por fila y evita cargar todos los planes en memoria.
        // Un plan Pendiente sin toma ya está en el punto de partida: no cuenta. Las obras
        // fuera de Planificación (Proyectadas) no ciclan: su plan queda como estaba hasta
        // que la obra vuelva (misma regla que el listado y el digest).
        var enPlanificacion = db.Obras.Where(ObraValidator.EnPlanificacion(hoy)).Select(o => o.Id);
        var reiniciados = await db.Planificaciones
            .Where(p => !p.ObraFinalizada && enPlanificacion.Contains(p.ObraId)
                && (p.Estado != EstadoPlanificacion.Pendiente || p.FechaTomaConocimiento != null))
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Estado, EstadoPlanificacion.Pendiente)
                .SetProperty(p => p.FechaCarga, (DateTime?)null)
                .SetProperty(p => p.FechaAprobacion, (DateTime?)null)
                .SetProperty(p => p.MotivoRevision, (string?)null)
                .SetProperty(p => p.Correcciones, (string?)null)
                .SetProperty(p => p.FechaTomaConocimiento, (DateTime?)null)
                .SetProperty(p => p.TomadaConocimientoPor, (string?)null), ct);

        db.RolloversPlanificacionEjecutados.Add(new RolloverPlanificacionEjecutado
        {
            Anio = hoy.Year,
            Mes = hoy.Month,
            FechaEjecucion = DateTime.UtcNow,
            CantidadPlanesReiniciados = reiniciados
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Otra instancia registró el mes primero (índice único): su corrida vale.
            // Los resets son idempotentes (poner en null lo que ya está en null).
        }

        return reiniciados;
    }
}
