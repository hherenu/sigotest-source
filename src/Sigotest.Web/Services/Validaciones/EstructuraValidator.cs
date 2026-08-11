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
