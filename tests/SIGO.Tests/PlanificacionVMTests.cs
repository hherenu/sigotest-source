using SIGO.Models.Enums;
using SIGO.Models.ViewModels;

namespace SIGO.Tests;

/// <summary>
/// Helpers puros del VM de la grilla de planificación. Cubren la lógica de totales que la
/// página no puede testear directamente: la suma de la curva fuera del horizonte renderizado
/// (que se preserva al guardar y entra al export/snapshot, y por lo tanto debe entrar también
/// al total en pantalla) y el mes del anticipo.
/// </summary>
public class PlanificacionVMTests
{
    private static BloqueAutorizanteVM Bloque(params (ConceptoPlanMonto, int?, int?, Moneda, decimal)[] montos)
    {
        var b = new BloqueAutorizanteVM { AutorizanteId = 1 };
        foreach (var (c, anio, mes, mon, monto) in montos)
            b.Valores[(c, anio, mes, mon)] = monto;
        return b;
    }

    private static HashSet<(ConceptoPlanMonto Concepto, int? Anio, int? Mes)> Render(
        params (ConceptoPlanMonto, int?, int?)[] periodos) => [.. periodos];

    [Fact]
    public void CurvaFueraDeHorizonte_SumaSoloLosPeriodosNoRenderizados()
    {
        var b = Bloque(
            (ConceptoPlanMonto.Mensual, 2030, 1, Moneda.Pesos, 100m),   // dentro del horizonte
            (ConceptoPlanMonto.Mensual, 2035, 6, Moneda.Pesos, 999m),   // fuera del horizonte
            (ConceptoPlanMonto.CalculoAnual, 2036, null, Moneda.Pesos, 50m)); // anual fuera del horizonte

        var render = Render((ConceptoPlanMonto.Mensual, 2030, 1));

        // 2030-01 está renderizado (se suma en vivo); 2035-06 y el anual 2036 quedan fuera.
        Assert.Equal(1049m, b.CurvaFueraDeHorizonte(Moneda.Pesos, render));
    }

    [Fact]
    public void CurvaFueraDeHorizonte_ExcluyeAnticipoYMontoAutorizado()
    {
        var b = Bloque(
            (ConceptoPlanMonto.MontoAutorizado, null, null, Moneda.Pesos, 1000m),
            (ConceptoPlanMonto.AnticipoFinanciero, null, null, Moneda.Pesos, 500m),
            (ConceptoPlanMonto.AnticipoFinanciero, 2035, 6, Moneda.Pesos, 300m), // anticipo con mes fuera de horizonte
            (ConceptoPlanMonto.Mensual, 2035, 6, Moneda.Pesos, 40m));

        // Solo la curva (Mensual/CalculoAnual) cuenta: ni el autorizado ni el anticipo (con o sin mes).
        Assert.Equal(40m, b.CurvaFueraDeHorizonte(Moneda.Pesos, Render()));
    }

    [Fact]
    public void CurvaFueraDeHorizonte_FiltraPorMoneda()
    {
        var b = Bloque(
            (ConceptoPlanMonto.Mensual, 2035, 6, Moneda.Pesos, 100m),
            (ConceptoPlanMonto.Mensual, 2035, 6, Moneda.USD, 7m));

        Assert.Equal(100m, b.CurvaFueraDeHorizonte(Moneda.Pesos, Render()));
        Assert.Equal(7m, b.CurvaFueraDeHorizonte(Moneda.USD, Render()));
    }

    [Fact]
    public void CurvaFueraDeHorizonte_ConTodoRenderizado_EsCero()
    {
        var b = Bloque(
            (ConceptoPlanMonto.Mensual, 2030, 1, Moneda.Pesos, 100m),
            (ConceptoPlanMonto.CalculoAnual, 2031, null, Moneda.Pesos, 50m));

        var render = Render(
            (ConceptoPlanMonto.Mensual, 2030, 1),
            (ConceptoPlanMonto.CalculoAnual, 2031, null));

        Assert.Equal(0m, b.CurvaFueraDeHorizonte(Moneda.Pesos, render));
    }

    [Fact]
    public void DetalleFueraDeHorizonte_ListaSoloLaCurvaNoRenderizada_EnOrdenCronologico()
    {
        var b = Bloque(
            (ConceptoPlanMonto.MontoAutorizado, null, null, Moneda.Pesos, 1000m), // nunca
            (ConceptoPlanMonto.AnticipoFinanciero, null, null, Moneda.Pesos, 500m), // nunca
            (ConceptoPlanMonto.Mensual, 2030, 1, Moneda.Pesos, 100m),   // renderizado
            (ConceptoPlanMonto.Mensual, 2036, 2, Moneda.USD, 7m),       // fuera
            (ConceptoPlanMonto.Mensual, 2035, 6, Moneda.Pesos, 999m),   // fuera
            (ConceptoPlanMonto.CalculoAnual, 2036, null, Moneda.Pesos, 50m)); // fuera

        var detalle = b.DetalleFueraDeHorizonte(Render((ConceptoPlanMonto.Mensual, 2030, 1)));

        // Cronológico; el anual (Mes null) del mismo año va antes que el mensual.
        Assert.Equal(
            [(2035, 6, Moneda.Pesos, 999m), (2036, null, Moneda.Pesos, 50m), (2036, 2, Moneda.USD, 7m)],
            detalle);
    }

    [Fact]
    public void CurvaFueraDeHorizonte_EsLaSumaDelDetalle()
    {
        var b = Bloque(
            (ConceptoPlanMonto.Mensual, 2035, 6, Moneda.Pesos, 100m),
            (ConceptoPlanMonto.CalculoAnual, 2036, null, Moneda.Pesos, 50m),
            (ConceptoPlanMonto.Mensual, 2035, 6, Moneda.USD, 7m));

        var render = Render();
        Assert.Equal(
            b.DetalleFueraDeHorizonte(render).Where(f => f.Moneda == Moneda.Pesos).Sum(f => f.Monto),
            b.CurvaFueraDeHorizonte(Moneda.Pesos, render));
        Assert.Equal(150m, b.CurvaFueraDeHorizonte(Moneda.Pesos, render));
    }

    [Fact]
    public void MesAnticipo_ConMesesDivergentesPorMoneda_EsDeterministicoYTomaElMenor()
    {
        var b = Bloque(
            (ConceptoPlanMonto.AnticipoFinanciero, 2026, 5, Moneda.Pesos, 200m),
            (ConceptoPlanMonto.AnticipoFinanciero, 2026, 3, Moneda.USD, 10m));

        Assert.Equal(new PeriodoVM(2026, 3), b.MesAnticipo());
    }

    [Fact]
    public void MesAnticipo_SinMesAsignado_EsNull()
    {
        var b = Bloque((ConceptoPlanMonto.AnticipoFinanciero, null, null, Moneda.Pesos, 500m));
        Assert.Null(b.MesAnticipo());
    }

    [Fact]
    public void ValorAnticipo_SumaTodasLasFilasDeLaMoneda_SinImportarPeriodo()
    {
        var b = Bloque(
            (ConceptoPlanMonto.AnticipoFinanciero, null, null, Moneda.Pesos, 500m),
            (ConceptoPlanMonto.AnticipoFinanciero, 2026, 3, Moneda.Pesos, 200m),
            (ConceptoPlanMonto.AnticipoFinanciero, 2026, 3, Moneda.USD, 10m));

        Assert.Equal(700m, b.ValorAnticipo(Moneda.Pesos));
        Assert.Equal(10m, b.ValorAnticipo(Moneda.USD));
    }
}
