namespace SIGO.Services;

/// <summary>
/// Base de las tareas mensuales de fondo (digest y rollover de planificación):
/// chequeo al arrancar (catch-up dentro del mismo mes) y después una vez por hora —
/// alcanza de sobra para un evento mensual y tolera cambios de config sin reiniciar.
/// Un fallo se loguea y se reintenta en la próxima pasada; cada implementación es
/// idempotente vía su tabla de registro (DigestsPlanificacionEnviados /
/// RolloversPlanificacionEjecutados), así los reinicios no repiten el evento.
/// </summary>
public abstract class TareaMensualHostedService(ILogger logger) : BackgroundService
{
    /// <summary>Nombre de la tarea para el log de errores (ej. "el chequeo del digest de planificación").</summary>
    protected abstract string Descripcion { get; }

    /// <summary>Una pasada: decide si este mes ya corrió y, si no, ejecuta el evento.</summary>
    protected abstract Task ChequearAsync(CancellationToken ct);

    protected sealed override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ChequearAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falló {Tarea}; se reintenta en la próxima pasada.", Descripcion);
            }

            try { await Task.Delay(TimeSpan.FromHours(1), ct); }
            catch (OperationCanceledException) { return; }
        }
    }
}
