using SIGO.Models.ViewModels;

namespace SIGO.Services.Validaciones;

/// <summary>
/// Reglas puras de las estructuras de costos y sus ítems, sin acceso a datos: el
/// servicio las aplica al guardar y los tests las fijan sin necesidad de base.
/// Los mensajes coinciden con los RequiredValidator de las páginas (defensa en
/// profundidad: la UI bloquea antes, pero un servicio invocado desde otro lado no
/// puede saltarse la regla).
/// </summary>
public static class EstructuraValidator
{
    public static string? NombreEstructura(string? nombre) =>
        string.IsNullOrWhiteSpace(nombre) ? "El nombre es obligatorio" : null;

    public static string? DescripcionItem(string? descripcion) =>
        string.IsNullOrWhiteSpace(descripcion) ? "La descripción es obligatoria" : null;

    /// <summary>
    /// El padre de un ítem debe ser un agrupador de su misma estructura y, si el ítem es
    /// un agrupador, no puede estar debajo de él: un ciclo dejaría al rubro y a todo lo
    /// que cuelga de él fuera del árbol que muestran las páginas. La UI solo ofrece
    /// agrupadores de la estructura, pero el servicio no puede confiar en eso.
    /// <paramref name="itemId"/> es 0 en el alta.
    /// </summary>
    public static string? Padre(int? padreId, int itemId,
        IReadOnlyCollection<(int Id, bool EsAgrupador, int? PadreId)> itemsDeLaEstructura)
    {
        if (padreId is not int p) return null;
        if (p == itemId) return "Un ítem no puede ser su propio agrupador padre.";

        var porId = itemsDeLaEstructura.ToDictionary(i => i.Id);
        if (!porId.TryGetValue(p, out var padre))
            return "El agrupador padre no pertenece a esta estructura.";
        if (!padre.EsAgrupador)
            return "El padre elegido no es un agrupador.";

        // Subida por los ancestros del padre: si aparece el ítem, quedaría debajo de sí mismo.
        var vistos = new HashSet<int>();
        for (var a = padre.PadreId; a is int id && vistos.Add(id); a = porId.TryGetValue(id, out var x) ? x.PadreId : null)
            if (id == itemId)
                return "El agrupador padre está dentro de este ítem: se formaría un ciclo.";
        return null;
    }

    /// <summary>Resultado de <see cref="Normalizar(bool,int?,string?,string?,string?,decimal?,decimal?,bool)"/>.</summary>
    public sealed record ItemNormalizado(
        int? AgrupadorPadreId, string? Codigo, string Descripcion,
        string? Unidad, decimal? Cantidad, decimal? PUBasico, decimal? Monto);

    /// <summary>
    /// Monto = Cantidad × PU redondeado a 4 decimales, solo si AMBOS existen (un ítem
    /// global "1 gl" puede tener PU sin cantidad, o cantidad sin PU: no hay monto que
    /// derivar). Los agrupadores nunca tienen monto propio: solo suman a sus hijos.
    /// </summary>
    public static decimal? CalcularMonto(bool esAgrupador, decimal? cantidad, decimal? puBasico) =>
        !esAgrupador && cantidad.HasValue && puBasico.HasValue
            ? Math.Round(cantidad.Value * puBasico.Value, 4)
            : null;

    /// <summary>
    /// Normaliza los campos de un ítem antes de persistir: los agrupadores no llevan
    /// código/unidad/cantidad/PU/monto (son cabeceras de rubro que solo suman hijos),
    /// los textos van recortados y el monto se deriva con <see cref="CalcularMonto"/>.
    ///
    /// <paramref name="conservarPadreDeAgrupador"/>: en el ALTA un agrupador nace en la
    /// raíz (false → padre null); en la EDICIÓN el padre se conserva tal como vino
    /// (true) — los agrupadores anidados existen (sub-rubros) y el form de agrupador no
    /// tiene campo de padre: forzarlo a null reparentaría el sub-rubro a raíz con solo
    /// editarle la descripción.
    /// </summary>
    public static ItemNormalizado Normalizar(
        bool esAgrupador, int? agrupadorPadreId, string? codigo, string? descripcion,
        string? unidad, decimal? cantidad, decimal? puBasico, bool conservarPadreDeAgrupador = false)
    {
        return new ItemNormalizado(
            AgrupadorPadreId: esAgrupador
                ? (conservarPadreDeAgrupador ? agrupadorPadreId : null)
                : agrupadorPadreId,
            Codigo: esAgrupador ? null : codigo?.Trim(),
            Descripcion: (descripcion ?? string.Empty).Trim(),
            Unidad: esAgrupador ? null : unidad?.Trim(),
            Cantidad: esAgrupador ? null : cantidad,
            PUBasico: esAgrupador ? null : puBasico,
            Monto: CalcularMonto(esAgrupador, cantidad, puBasico));
    }

    /// <summary>Sobrecarga de conveniencia sobre el VM (mismas reglas, sin mutarlo).</summary>
    public static ItemNormalizado Normalizar(ItemEstructuraVM vm, bool conservarPadreDeAgrupador = false) =>
        Normalizar(vm.EsAgrupador, vm.AgrupadorPadreId, vm.Codigo, vm.Descripcion,
            vm.Unidad, vm.Cantidad, vm.PUBasico, conservarPadreDeAgrupador);
}
