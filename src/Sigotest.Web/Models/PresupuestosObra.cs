using SIGO.Models.Enums;

namespace SIGO.Models;

/// <summary>
/// Presupuestos de la obra por moneda: oficial (en pesos obligatorio; US$ y € opcionales)
/// y adjudicado (opcional). ÚNICA implementación de "qué presupuesto aplica": si hay
/// adjudicado cargado (algún importe en cualquier moneda), manda en TODAS las monedas
/// —una moneda sin importe en el adjudicado es 0, no cae al oficial—; si no, aplica el
/// oficial. Es el autorizado de la Obra Básica en Planificación y la base del tope del
/// 50% de adicionales/BED. Puro: la entidad y los VMs lo construyen desde sus campos.
/// </summary>
public record PresupuestosObra(
    decimal OficialPesos, decimal? OficialUSD, decimal? OficialEUR,
    decimal? AdjudicadoPesos, decimal? AdjudicadoUSD, decimal? AdjudicadoEUR)
{
    /// <summary>true si el adjudicado está cargado (algún importe en cualquier moneda).</summary>
    public bool Adjudicado => AdjudicadoPesos.HasValue || AdjudicadoUSD.HasValue || AdjudicadoEUR.HasValue;

    public decimal Oficial(Moneda moneda) => moneda switch
    {
        Moneda.Pesos => OficialPesos,
        Moneda.USD => OficialUSD ?? 0m,
        Moneda.EUR => OficialEUR ?? 0m,
        _ => 0m
    };

    public decimal AdjudicadoEn(Moneda moneda) => moneda switch
    {
        Moneda.Pesos => AdjudicadoPesos ?? 0m,
        Moneda.USD => AdjudicadoUSD ?? 0m,
        Moneda.EUR => AdjudicadoEUR ?? 0m,
        _ => 0m
    };

    /// <summary>Presupuesto que aplica en la moneda: el adjudicado si está cargado, si no el oficial.</summary>
    public decimal Referencia(Moneda moneda) => Adjudicado ? AdjudicadoEn(moneda) : Oficial(moneda);

    /// <summary>true si el presupuesto que aplica tiene algún importe (la obra tiene presupuesto cargado).</summary>
    public bool TieneReferencia => Enum.GetValues<Moneda>().Any(m => Referencia(m) > 0);

    /// <summary>"Presupuesto adjudicado" u "oficial", según cuál aplique.</summary>
    public string Etiqueta => Adjudicado ? "Presupuesto adjudicado" : "Presupuesto oficial";

    public static PresupuestosObra De(Obra o) => new(
        o.PresupuestoOficial, o.PresupuestoOficialUSD, o.PresupuestoOficialEUR,
        o.PresupuestoAdjudicado, o.PresupuestoAdjudicadoUSD, o.PresupuestoAdjudicadoEUR);
}
