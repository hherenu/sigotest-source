using SIGO.Models.Enums;

namespace SIGO.Services.Validaciones;

/// <summary>
/// Reglas del ciclo de vida del certificado (Borrador → Cerrado → Aprobado), puras y
/// sin acceso a datos: el servicio les pasa lo que ya leyó y lanza si devuelven error;
/// la UI puede evaluarlas para deshabilitar acciones con el mismo criterio.
/// </summary>
public static class CertificadoValidator
{
    public static bool EsCerrado(EstadoCertificado e) =>
        e is EstadoCertificado.Cerrado or EstadoCertificado.Aprobado;

    public static string? Guardar(EstadoCertificado estado) =>
        estado != EstadoCertificado.Borrador
            ? "Solo se pueden editar certificados en borrador."
            : null;

    /// <summary>
    /// Cierre en orden y sin huecos: el snapshot congela los acumulados de los cerrados
    /// anteriores, así que cerrar con un previo en borrador o inexistente congelaría un
    /// "Anterior" incompleto que nada recalcula después.
    /// </summary>
    public static string? Cerrar(EstadoCertificado estado, int numero,
        IEnumerable<(int Numero, EstadoCertificado Estado)> previos)
    {
        if (estado != EstadoCertificado.Borrador)
            return "Solo se puede cerrar un certificado en borrador.";

        var lista = previos.ToList();

        var faltante = Enumerable.Range(1, numero - 1)
            .Except(lista.Select(p => p.Numero))
            .Order()
            .Cast<int?>()
            .FirstOrDefault();
        if (faltante is not null)
            return $"No se puede cerrar el certificado N° {numero}: no existe el N° {faltante}. Los certificados se cierran en orden y sin huecos.";

        var abiertos = lista.Where(p => !EsCerrado(p.Estado)).OrderBy(p => p.Numero).ToList();
        if (abiertos.Count > 0)
        {
            var (nro, est) = abiertos[0];
            return $"No se puede cerrar el certificado N° {numero}: el N° {nro} " +
                   (est == EstadoCertificado.Borrador ? "sigue en borrador" : $"está {est}") +
                   ". Los certificados se cierran en orden.";
        }

        return null;
    }

    /// <summary>
    /// Los cerrados posteriores congelaron su "Anterior" incluyendo a este: si se reabre
    /// y modifica, esos snapshots quedan falsos sin que nada los recalcule.
    /// </summary>
    public static string? Reabrir(EstadoCertificado estado, int numero,
        IEnumerable<int> posterioresCerrados)
    {
        if (estado != EstadoCertificado.Cerrado)
            return "Solo se puede reabrir un certificado cerrado.";

        var lista = posterioresCerrados.Order().ToList();
        if (lista.Count > 0)
            return $"No se puede reabrir el certificado N° {numero}: el N° {lista[0]} ya está cerrado y su snapshot depende de este. Reabrí primero los posteriores, del último hacia atrás.";

        return null;
    }

    /// <summary>Rango de años del alta (el form de la página usa los mismos límites).</summary>
    public const int AnioMinimo = 2010;
    public const int AnioMaximo = 2099;

    /// <summary>
    /// Datos del alta que la página ya acota (número ≥ 1, mes 1-12, año en rango) y que el
    /// servicio no puede dar por buenos: un mes 13 persistido rompería después las
    /// pantallas que muestran el período (Fmt.MesNombre no lo admite). La fecha de emisión
    /// va en el mismo rango de años: un selector vaciado no puede guardar el 01/01/0001.
    /// </summary>
    public static string? Alta(int numero, int mes, int anio, DateTime fechaEmision)
    {
        if (numero < 1)
            return "El número de certificado debe ser 1 o mayor.";
        if (mes is < 1 or > 12)
            return "El mes debe estar entre 1 y 12.";
        if (anio is < AnioMinimo or > AnioMaximo)
            return $"El año debe estar entre {AnioMinimo} y {AnioMaximo}.";
        if (fechaEmision.Year is < AnioMinimo or > AnioMaximo)
            return $"La fecha de emisión debe estar entre {AnioMinimo} y {AnioMaximo}.";
        return null;
    }

    /// <summary>
    /// Numeración del alta: la cadena del "Anterior" exige números únicos, sin huecos
    /// hacia arriba y nunca por debajo del último cerrado (su acumulado ya quedó
    /// congelado sin el nuevo y nada lo recalcula).
    /// </summary>
    public static string? NumeroNuevo(int numero, bool yaExiste, int maxNumero, int ultimoCerrado)
    {
        if (yaExiste)
            return "Ya existe un certificado con ese número para esta obra.";

        if (numero > maxNumero + 1)
            return $"El N° {numero} dejaría un hueco en la numeración: el próximo número es {maxNumero + 1}.";

        if (numero <= ultimoCerrado)
            return $"El N° {numero} es anterior al último certificado cerrado (N° {ultimoCerrado}), cuyo acumulado ya quedó congelado sin él.";

        return null;
    }

    public static string? Aprobar(EstadoCertificado estado) =>
        estado != EstadoCertificado.Cerrado
            ? "Solo se puede aprobar un certificado cerrado."
            : null;

    public static string? Desaprobar(EstadoCertificado estado) =>
        estado != EstadoCertificado.Aprobado
            ? "Solo se puede volver a Cerrado un certificado aprobado."
            : null;

    /// <summary>Un cerrado integra la cadena de acumulados: debe reabrirse antes de eliminarse.</summary>
    public static string? Eliminar(EstadoCertificado estado, int numero) =>
        EsCerrado(estado)
            ? $"No se puede eliminar el certificado N° {numero}: está {estado}. Reabrilo primero si realmente hay que eliminarlo."
            : null;

    /// <summary>
    /// Agregar un bloque: solo en borrador, sin repetir estructura dentro del certificado
    /// y solo con estructuras de la misma obra (el servicio pasa los hechos que ya leyó).
    /// </summary>
    public static string? AgregarBloque(EstadoCertificado estado, bool bloqueDuplicado, bool estructuraDeLaObra)
    {
        if (estado != EstadoCertificado.Borrador)
            return "Solo se pueden agregar bloques en borrador.";
        if (bloqueDuplicado)
            return "Ese bloque ya está en el certificado.";
        if (!estructuraDeLaObra)
            return "Estructura inválida.";
        return null;
    }

    /// <summary>Quitar un bloque: solo mientras el certificado sigue en borrador.</summary>
    public static string? QuitarBloque(EstadoCertificado estado) =>
        estado != EstadoCertificado.Borrador
            ? "Solo se pueden quitar bloques en borrador."
            : null;

    /// <summary>
    /// Acumulado (anterior + mes) por encima del 100%: casi siempre es un error de tipeo
    /// (500 en vez de 5,00), así que la carga lo marca y pide confirmación, no lo prohíbe.
    /// El epsilon absorbe redondeos de 4 decimales; los negativos (BED/economías) son
    /// legítimos y no se marcan.
    /// </summary>
    public static bool ExcedeAvanceAcumulado(decimal? porcentajeAnterior, decimal porcentajeMes) =>
        (porcentajeAnterior ?? 0) + porcentajeMes > 100.0001m;
}
