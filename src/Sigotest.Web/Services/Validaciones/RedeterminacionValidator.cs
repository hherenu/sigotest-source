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

    /// <summary>Los disparos presentados/aprobados son valores oficiales: ni recálculo ni eliminación.</summary>
    public static bool EsRecalculable(EstadoRedeterminacion estado) =>
        estado is EstadoRedeterminacion.Borrador or EstadoRedeterminacion.Calculada;

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
            return $"El disparo {nroDisparo} está en estado {estado} y no admite recálculo.";

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
            return $"El disparo {nroDisparo} está en estado {estado} y no puede eliminarse.";

        if (hayPosterior)
            return $"Solo se puede eliminar el último disparo de la obra. El disparo {nroDisparo} tiene posteriores que acumulan su coeficiente.";

        return null;
    }
}
