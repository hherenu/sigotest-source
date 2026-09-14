using SIGO.Models;
using SIGO.Models.Enums;

namespace SIGO.Services.Validaciones;

/// <summary>
/// Reglas de la máquina de estados del circuito de planificación
/// (Pendiente → Cargada → Aprobada, con loop EnRevision), puras y sin acceso a datos
/// (mismo esquema que <see cref="CertificadoValidator"/>).
/// </summary>
public static class PlanificacionValidator
{
    /// <summary>
    /// La grilla (montos, mes del anticipo y bloques) solo se modifica en la primera
    /// instancia (Pendiente) y En revisión. Cargada, Aprobada y con toma de conocimiento
    /// son de solo lectura: lo que el Gerente controla y Presupuesto toma es lo que se
    /// ve. Para corregir, hay que volver a una instancia anterior (el Gerente envía a
    /// revisión, o el rollover mensual devuelve a Pendiente).
    /// </summary>
    public static string? EditarGrilla(EstadoPlanificacion estado) =>
        estado is EstadoPlanificacion.Pendiente or EstadoPlanificacion.EnRevision
            ? null
            : $"El plan está {EstadoTexto(estado)} y no se puede modificar. Solo se edita Pendiente o En revisión: para corregirlo, el Gerente debe enviarlo a revisión.";

    private static string EstadoTexto(EstadoPlanificacion estado) => estado switch
    {
        EstadoPlanificacion.EnRevision => "En revisión",
        _ => estado.ToString()
    };

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
    /// La Obra Básica se controla contra el presupuesto de la obra (adjudicado u
    /// oficial): sin presupuesto cargado no hay contra qué balancear, así que el plan no
    /// puede avanzar a Cargada/Aprobada. El guardado solo avisa.
    /// </summary>
    /// <summary>
    /// Una obra "Proyectada" no participa de Planificación (listado, digest, export): su
    /// plan tampoco puede cambiar de paso desde una pestaña abierta antes del cambio.
    /// </summary>
    public static string? ObraEnPlanificacion(string estadoObra) =>
        estadoObra == "Proyectada"
            ? "La obra está en estado Proyectada (sin antecedentes ni plazo contractual) y no participa de Planificación."
            : null;

    public static string? PresupuestoObra(PresupuestosObra presupuestos) =>
        !presupuestos.TieneReferencia
            ? "La obra no tiene presupuesto oficial cargado. Cargalo en la ficha de la obra para poder controlar la planificación."
            : null;

    /// <summary>
    /// Aviso (nunca bloquea) para adicionales/BED: el Monto Autorizado del bloque en una
    /// moneda no debería superar el 50% del presupuesto de referencia de la obra en esa
    /// moneda (adjudicado u oficial). Sin presupuesto en la moneda no hay contra qué comparar.
    /// </summary>
    public static string? AdicionalSobreLimite(string bloque, Moneda moneda, decimal autorizado,
        decimal presupuestoReferencia, string etiquetaPresupuesto)
    {
        if (presupuestoReferencia <= 0) return null;
        var limite = presupuestoReferencia * 0.5m;
        return autorizado > limite
            ? $"El Monto Autorizado de {bloque} en {moneda} ({autorizado:N2}) supera el 50% del {etiquetaPresupuesto.ToLowerInvariant()} ({limite:N2})."
            : null;
    }

    /// <summary>
    /// El anticipo financiero se paga a más tardar en el primer mes del plan (el inicio
    /// de la obra): un mes posterior es un error de carga. Recibe el mes asignado al
    /// anticipo de cada bloque (null = sin mes, datos anteriores a la mejora: no se
    /// valida) y el primer mes del horizonte. Como <see cref="Balanceado"/>, NO bloquea
    /// el guardado de la grilla (la página solo avisa); bloquea el cambio de paso a
    /// Cargada/Aprobada.
    /// </summary>
    public static string? MesAnticipo(IEnumerable<(string Bloque, int? Anio, int? Mes)> anticipos,
        int primerAnio, int primerMes)
    {
        var tardios = anticipos
            .Where(a => a.Anio.HasValue && a.Mes.HasValue
                && (a.Anio.Value, a.Mes.Value).CompareTo((primerAnio, primerMes)) > 0)
            .Select(a => $"{a.Bloque}: {a.Mes:00}/{a.Anio}")
            .ToList();
        if (tardios.Count == 0) return null;

        return $"El mes del anticipo no puede ser posterior al primer mes del plan ({primerMes:00}/{primerAnio}). "
            + string.Join("; ", tardios) + ".";
    }

    /// <summary>
    /// Regla "Autorizado vs Planificado = 0": en cada bloque y moneda, lo planificado
    /// (anticipo + curva completa, incluida la parte fuera del horizonte renderizado)
    /// debe igualar el Monto Autorizado. Recibe las diferencias Autorizado − Planificado
    /// ya calculadas (por el servicio o por la grilla); con alguna ≠ 0 devuelve el
    /// detalle. NO bloquea el guardado de la grilla (la página solo avisa); bloquea el
    /// cambio de paso a Cargada/Aprobada: un plan desbalanceado puede quedar registrado
    /// como borrador pero no avanzar en el circuito.
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
