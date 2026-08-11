namespace SIGO.Models.Enums;

/// <summary>Naturaleza del bloque de costos de una obra.</summary>
public enum TipoEstructura
{
    Basico,
    Adicional,
    BED,
    Demasia,
    Economia,
    Ajuste,
    Otro
}

/// <summary>
/// Tipo de movimiento de un ítem. Compartido entre ItemEstructura e ItemCertificado:
/// en el certificado normalmente se deriva del ítem de estructura, pero se permite
/// que difiera (ej: una compensación sobre un ítem Normal).
/// </summary>
public enum TipoMovimiento
{
    Normal,
    Adicional,
    Demasia,
    Economia,
    Compensacion,
    AjusteBED
}

/// <summary>Estado de una redeterminación guardada.</summary>
public enum EstadoRedeterminacion
{
    Borrador,
    Calculada,
    Presentada,
    AprobadaCCyR,
    AprobadaOS,
    Rechazada,
    Anulada
}

/// <summary>
/// Estado de un certificado. El snapshot de valores Anterior/Actual/Acumulado
/// se congela al pasar a <see cref="Cerrado"/>. "Cerrado" implica emitido.
/// </summary>
public enum EstadoCertificado
{
    Borrador,
    Cerrado,
    Aprobado,
    Anulado
}

// ── Módulo Planificación (contexto separado; ver memoria de arquitectura) ─────────

/// <summary>
/// Estado del flujo de la planificación de una obra. Dispara los mails del circuito:
/// Cargada → mail al Gerente de Obras; Aprobada → mail a Presupuesto, que al tomar
/// conocimiento genera el snapshot mensual (PlanificacionSnapshot). El plan vivo
/// sigue siendo editable en cualquier estado.
/// </summary>
public enum EstadoPlanificacion
{
    Pendiente,
    Cargada,
    EnRevision,
    Aprobada
}

/// <summary>Naturaleza del bloque autorizante dentro de una planificación.</summary>
public enum TipoAutorizante
{
    Basica,
    Adicional,
    BED
}

/// <summary>Qué representa una fila de monto de la planificación.</summary>
public enum ConceptoPlanMonto
{
    /// <summary>Monto autorizado/aprobado del bloque (sin mes).</summary>
    MontoAutorizado,
    /// <summary>Anticipo financiero del bloque (sin mes).</summary>
    AnticipoFinanciero,
    /// <summary>Monto planificado de un mes puntual (Anio + Mes).</summary>
    Mensual,
    /// <summary>Cálculo anual para la cola del horizonte (solo Anio).</summary>
    CalculoAnual
}

/// <summary>Moneda de un monto de la planificación.</summary>
public enum Moneda
{
    Pesos,
    USD,
    EUR
}

/// <summary>
/// Rol de aplicación de un usuario del dominio. Admin todo (incluye ABM de usuarios);
/// CCyR opera certificaciones y redeterminaciones; Director/Gerente/Presupuesto son
/// los actores del circuito de Planificación (cargar / aprobar-revisar / conocer).
/// </summary>
public enum RolUsuario
{
    Admin,
    Director,
    Gerente,
    Presupuesto,
    CCyR
}
