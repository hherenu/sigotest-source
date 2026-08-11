using SIGO.Models.Enums;

namespace SIGO.Services.Validaciones;

/// <summary>
/// Reglas de la máquina de estados del circuito de planificación
/// (Pendiente → Cargada → Aprobada, con loop EnRevision), puras y sin acceso a datos
/// (mismo esquema que <see cref="CertificadoValidator"/>).
/// </summary>
public static class PlanificacionValidator
{
    /// <summary>La obra básica es única en el plan; adicionales/BED autonumeran.</summary>
    public static string? AgregarBloque(TipoAutorizante tipo, bool yaTieneBasica) =>
        tipo == TipoAutorizante.Basica && yaTieneBasica
            ? "La obra básica ya existe en el plan."
            : null;

    /// <summary>Guardar la grilla sin bloques no registra nada: se rechaza con mensaje claro.</summary>
    public static string? GuardarGrilla(int cantidadBloques) =>
        cantidadBloques == 0
            ? "El plan no tiene bloques: agregá al menos uno (Obra Básica, Adicional o BED) antes de guardar."
            : null;

    public static string? MarcarCargada(EstadoPlanificacion estado, int cantidadBloques)
    {
        if (estado is not (EstadoPlanificacion.Pendiente or EstadoPlanificacion.EnRevision))
            return "Solo se puede cargar un plan Pendiente o En Revisión.";

        // Un plan sin bloques no puede darse por cargado.
        if (cantidadBloques == 0)
            return "El plan no tiene bloques cargados.";

        return null;
    }

    /// <summary>
    /// Desde Aprobada también: es el camino de salida de una aprobación por error
    /// (sin esto el estado Aprobada quedaba sin ninguna transición en la UI).
    /// El motivo es obligatorio whitespace-safe (el RequiredValidator del form deja
    /// pasar espacios): sin motivo, el mail al director sale en blanco y la alerta
    /// "En revisión" no se muestra.
    /// </summary>
    public static string? EnviarARevision(EstadoPlanificacion estado, string? motivo)
    {
        if (estado is not (EstadoPlanificacion.Cargada or EstadoPlanificacion.Aprobada))
            return "Solo se puede enviar a revisión un plan Cargado o Aprobado.";

        if (string.IsNullOrWhiteSpace(motivo))
            return "El motivo de la revisión es obligatorio.";

        return null;
    }

    public static string? Aprobar(EstadoPlanificacion estado) =>
        estado != EstadoPlanificacion.Cargada
            ? "Solo se puede aprobar un plan Cargado."
            : null;

    public static string? TomarConocimiento(EstadoPlanificacion estado, DateTime? fechaTomaConocimiento)
    {
        if (estado != EstadoPlanificacion.Aprobada)
            return "Solo se puede tomar conocimiento de un plan Aprobado.";

        if (fechaTomaConocimiento is not null)
            return "La toma de conocimiento de este ciclo ya fue registrada.";

        return null;
    }

    /// <summary>
    /// Regla "Autorizado vs Planificado = 0": en cada bloque y moneda, lo planificado
    /// (anticipo + curva completa, incluida la parte fuera del horizonte renderizado)
    /// debe igualar el Monto Autorizado. Recibe las diferencias Autorizado − Planificado
    /// ya calculadas por el servicio; con alguna ≠ 0 devuelve el detalle. Bloquea el
    /// guardado y las transiciones a Cargada/Aprobada: un plan desbalanceado no puede
    /// quedar registrado ni avanzar en el circuito.
    /// </summary>
    public static string? Balanceado(IEnumerable<(string Bloque, Moneda Moneda, decimal Diferencia)> diferencias)
    {
        var desbalances = diferencias.Where(d => d.Diferencia != 0m).ToList();
        if (desbalances.Count == 0) return null;

        var detalle = string.Join("; ", desbalances.Select(d =>
            $"{d.Bloque} en {d.Moneda}: {(d.Diferencia > 0 ? "falta planificar" : "planificado de más")} {Math.Abs(d.Diferencia):N2}"));
        return $"Autorizado vs Planificado debe dar 0 en todos los bloques. {detalle}.";
    }
}
