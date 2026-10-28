using SIGO.Models.Enums;

namespace SIGO.Services.Validaciones;

/// <summary>
/// Reglas del cálculo y guardado de disparos de redeterminación, puras y sin acceso
/// a datos (mismo esquema que <see cref="CertificadoValidator"/>).
/// </summary>
public static class RedeterminacionValidator
{
    /// <summary>Con pesos que no suman 100 el TotalKiK0 sale sesgado sin que nada lo delate.</summary>
    public static string? Calcular(decimal sumaPesos) =>
        sumaPesos != 100m
            ? $"La tabla de ponderación suma {sumaPesos:N2}% y debe sumar 100%. Corregí los pesos antes de calcular."
            : null;

    /// <summary>
    /// El mes de salto tiene que ser posterior al mes base: con el salto antes (base dic-26,
    /// salto jun-26) el coeficiente sale invertido y contamina la cadena de variación
    /// acumulada de los disparos siguientes; con el mismo mes da 1 y no es un salto.
    /// </summary>
    public static string? Periodos(int anioBase, int mesBase, int anioSalto, int mesSalto) =>
        (anioSalto, mesSalto).CompareTo((anioBase, mesBase)) > 0
            ? null
            : $"El mes de salto ({mesSalto:00}/{anioSalto}) tiene que ser posterior al mes base ({mesBase:00}/{anioBase}).";

    /// <summary>
    /// Un cálculo con índices faltantes no tiene coeficiente total: guardarlo consumiría
    /// un NroDisparo y entraría a la cadena de variación acumulada con un valor nulo que
    /// los guardados posteriores saltean en silencio.
    /// </summary>
    public static string? GuardarDisparo(decimal? totalKiK0) =>
        totalKiK0 is null
            ? "Faltan valores de índice para algunos insumos: el cálculo no tiene coeficiente total y no puede guardarse como disparo."
            : null;

    public static string? RecalcularDisparo(decimal? totalKiK0) =>
        totalKiK0 is null
            ? "Faltan valores de índice para algunos insumos: el cálculo no tiene coeficiente total y no puede guardarse."
            : null;

    /// <summary>
    /// El recálculo reemplaza el cálculo del disparo, nunca lo muda de obra: reasignarlo
    /// dejaría la cadena de VariacionAcumulada de la obra origen multiplicando un
    /// coeficiente que ya no está, y su numeración oficial con hueco.
    /// </summary>
    public static string? MismaObra(int obraIdDisparo, int obraId) =>
        obraIdDisparo != obraId
            ? "El disparo pertenece a otra obra y no puede reasignarse."
            : null;

    /// <summary>
    /// La tabla de ponderación es de la obra del disparo: una de otra obra (o inexistente)
    /// dejaría una referencia cruzada persistida en la redeterminación guardada.
    /// </summary>
    public static string? TablaDeLaObra(int? tablaObraId, int obraId) =>
        tablaObraId != obraId
            ? "La tabla de ponderación no pertenece a la obra del disparo."
            : null;

    /// <summary>
    /// Los disparos presentados/aprobados son valores oficiales, y los rechazados/anulados
    /// quedan como historia del trámite: ni recálculo ni eliminación.
    /// </summary>
    public static bool EsRecalculable(EstadoRedeterminacion estado) =>
        estado is EstadoRedeterminacion.Borrador or EstadoRedeterminacion.Calculada;

    /// <summary>
    /// Un disparo rechazado o anulado no cuenta: ni para la numeración (el próximo que se
    /// guarde usa su número) ni para la cadena de variación acumulada, ni como «último».
    /// </summary>
    public static bool EsVigente(EstadoRedeterminacion estado) =>
        estado is not (EstadoRedeterminacion.Rechazada or EstadoRedeterminacion.Anulada);

    /// <summary>
    /// Estado del trámite según los datos administrativos: la baja (rechazo o anulación)
    /// manda; si no, la aprobación por OS, la de CCyR o el expediente VR cargado
    /// (presentada). Sin nada de eso el disparo está Calculado y admite recálculo: vaciar
    /// esos datos es la forma de corregir una carga equivocada.
    /// </summary>
    public static EstadoRedeterminacion EstadoAdministrativo(
        EstadoRedeterminacion? baja, string? nroExpedienteVR, DateTime? fechaAprobacionCCyR, DateTime? fechaAprobacionOS) =>
        baja is EstadoRedeterminacion.Rechazada or EstadoRedeterminacion.Anulada ? baja.Value
        : fechaAprobacionOS is not null ? EstadoRedeterminacion.AprobadaOS
        : fechaAprobacionCCyR is not null ? EstadoRedeterminacion.AprobadaCCyR
        : !string.IsNullOrWhiteSpace(nroExpedienteVR) ? EstadoRedeterminacion.Presentada
        : EstadoRedeterminacion.Calculada;

    /// <summary>
    /// Dar de baja (rechazar o anular) un disparo vigente, o volver a ponerlo vigente.
    /// Solo el último vigente se da de baja: los posteriores acumulan su coeficiente. Y
    /// vuelve a estar vigente solo si su número no lo tomó otro disparo y no hay vigentes
    /// posteriores: la numeración y la cadena quedarían inconsistentes.
    /// </summary>
    public static string? CambioDeVigencia(EstadoRedeterminacion antes, EstadoRedeterminacion despues, int nroDisparo,
        bool hayVigentePosterior, bool hayOtroVigenteConElMismoNumero)
    {
        if (EsVigente(antes) && !EsVigente(despues) && hayVigentePosterior)
            return $"Solo se puede {(despues == EstadoRedeterminacion.Anulada ? "anular" : "rechazar")} el último disparo vigente de la obra. " +
                   $"El disparo {nroDisparo} tiene posteriores que acumulan su coeficiente.";

        if (!EsVigente(antes) && EsVigente(despues) && (hayOtroVigenteConElMismoNumero || hayVigentePosterior))
            return $"El disparo {nroDisparo} no puede volver a quedar vigente: la obra ya tiene " +
                   (hayOtroVigenteConElMismoNumero ? $"otro disparo vigente con el número {nroDisparo}." : "disparos vigentes posteriores.");

        return null;
    }

    /// <summary>Texto del estado para pantallas y mensajes.</summary>
    public static string TextoEstado(EstadoRedeterminacion estado) => estado switch
    {
        EstadoRedeterminacion.AprobadaCCyR => "Aprobada CCyR",
        EstadoRedeterminacion.AprobadaOS => "Aprobada OS",
        _ => estado.ToString()
    };

    private static string NoAdmite(EstadoRedeterminacion estado, int nroDisparo, string accion) =>
        $"El disparo {nroDisparo} está en estado «{TextoEstado(estado)}» y {accion}." +
        (EsVigente(estado) ? " Si el expediente o las fechas se cargaron por error, se borran desde sus datos administrativos." : "");

    /// <summary>
    /// Un disparo presentado/aprobado es un valor oficial: recalcularlo pisaría su
    /// snapshot sin rastro. Solo Borrador/Calculada admiten recálculo, y solo el
    /// ÚLTIMO de la obra (misma regla que <see cref="EliminarDisparo"/>): cambiar el
    /// coeficiente o el tramo de uno intermedio reescribiría la VariacionAcumulada
    /// de todos los posteriores —incluidos los oficiales— y rompería el encadenado
    /// base→salto de sus tramos.
    /// </summary>
    public static string? AdmiteRecalculo(EstadoRedeterminacion estado, int nroDisparo, bool hayPosterior)
    {
        if (!EsRecalculable(estado))
            return NoAdmite(estado, nroDisparo, "no admite recálculo");

        if (hayPosterior)
            return $"Solo se puede recalcular el último disparo de la obra. El disparo {nroDisparo} tiene posteriores que acumulan su coeficiente.";

        return null;
    }

    /// <summary>
    /// Solo el último disparo de la obra puede eliminarse: los posteriores acumulan la
    /// variación multiplicando los coeficientes previos, y eliminar uno intermedio
    /// dejaría esa cadena (y la numeración oficial) inconsistente.
    /// </summary>
    public static string? EliminarDisparo(EstadoRedeterminacion estado, int nroDisparo, bool hayPosterior)
    {
        if (!EsRecalculable(estado))
            return NoAdmite(estado, nroDisparo, "no puede eliminarse");

        if (hayPosterior)
            return $"Solo se puede eliminar el último disparo de la obra. El disparo {nroDisparo} tiene posteriores que acumulan su coeficiente.";

        return null;
    }
}
