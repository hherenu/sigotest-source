using System.Linq.Expressions;
using SIGO.Models;

namespace SIGO.Services.Validaciones;

/// <summary>
/// Reglas puras de la obra, sin acceso a datos: el servicio les pasa lo que ya leyó
/// y lanza si devuelven error; la UI puede evaluarlas con el mismo criterio.
/// </summary>
public static class ObraValidator
{
    /// <summary>
    /// Estado computado de la obra, en orden de precedencia:
    /// "Proceso Licitatorio" si tiene antecedentes (expediente) y el acta de inicio es
    /// posterior a hoy (todavía no arrancó); si no, según el plazo contractual:
    /// "Vigente" mientras la fecha final no pasó y "Plazo Vencido" después; sin fecha
    /// final, "Proyectada".
    /// ÚNICA implementación (ObraVM y ObraOpcionVM delegan acá); `hoy` viene por
    /// parámetro para que la regla sea testeable sin depender del reloj.
    /// </summary>
    public static string Estado(string? antecedentes, DateTime? fechaActaInicio,
        DateTime? fechaFinalContrato, DateTime hoy)
    {
        if (!string.IsNullOrWhiteSpace(antecedentes)
            && fechaActaInicio.HasValue && fechaActaInicio.Value.Date > hoy.Date)
            return "Proceso Licitatorio";

        if (fechaFinalContrato.HasValue)
            return fechaFinalContrato.Value.Date >= hoy.Date ? "Vigente" : "Plazo Vencido";

        return "Proyectada";
    }

    /// <summary>
    /// Predicado traducible a SQL: obras cuyo estado NO es "Proyectada" (las únicas que
    /// participan del módulo de Planificación). Es el espejo de <see cref="Estado"/>
    /// sobre la entidad —ObraValidatorTests verifica que ambos coincidan—; si cambia
    /// la regla del estado, cambia acá también.
    /// </summary>
    public static Expression<Func<Obra, bool>> EnPlanificacion(DateTime hoy)
    {
        var hoyFecha = hoy.Date;
        return o => o.FechaFinalContrato != null
            || (!string.IsNullOrWhiteSpace(o.Antecedentes)
                && o.FechaActaInicio != null && o.FechaActaInicio.Value.Date > hoyFecha);
    }

    /// <summary>
    /// Campos mínimos del alta/edición. Defensa en profundidad detrás de los
    /// RequiredValidator del formulario (mismos mensajes): un servicio invocado desde
    /// otro lado no puede guardar una obra sin identificar.
    /// </summary>
    public static string? Guardar(string? nombre, string? numeroLicitacion)
    {
        if (string.IsNullOrWhiteSpace(nombre))
            return "El nombre es obligatorio";
        if (string.IsNullOrWhiteSpace(numeroLicitacion))
            return "La licitación es obligatoria";
        return null;
    }

    /// <summary>
    /// Mensaje específico antes de chocar con las FK Restrict al eliminar: qué
    /// historial tiene la obra y por qué no puede eliminarse. Null si no tiene
    /// dependencias y puede borrarse.
    /// </summary>
    public static string? Eliminar(int certificados, int redeterminaciones, int estructuras,
        int tablasPonderacion, int planificaciones)
    {
        var partes = new List<string>();
        if (certificados > 0) partes.Add($"{certificados} certificado(s)");
        if (redeterminaciones > 0) partes.Add($"{redeterminaciones} redeterminación(es)");
        if (estructuras > 0) partes.Add($"{estructuras} estructura(s) de costos");
        if (tablasPonderacion > 0) partes.Add("tabla de ponderación");
        if (planificaciones > 0) partes.Add("planificación");

        return partes.Count > 0
            ? $"La obra tiene {string.Join(", ", partes)} asociados. "
              + "Eliminá primero ese historial si realmente corresponde borrarla."
            : null;
    }
}
