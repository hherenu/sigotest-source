using SIGO.Models.Enums;
using SIGO.Models.ViewModels;

namespace SIGO.Tests;

/// <summary>
/// Agregados en vivo de la grilla de planificación (TotalesPlan): la aritmética que
/// antes vivía en el @code de PlanificacionEditar. Cubre que "Planificado" combine
/// anticipo + períodos renderizados (celdas de la sesión) + curva guardada fuera del
/// horizonte (bloque), y los totales por tipo y generales.
/// </summary>
public class TotalesPlanTests
{
    private static readonly PeriodoVM[] Periodos = [new(2030, 1), new(2030, 2), new(2031, null)];

    private static Dictionary<(int, ConceptoPlanMonto, int?, int?, Moneda), decimal?> Celdas(
        params (int, ConceptoPlanMonto, int?, int?, Moneda, decimal?)[] valores)
    {
        var d = new Dictionary<(int, ConceptoPlanMonto, int?, int?, Moneda), decimal?>();
        foreach (var (aut, c, anio, mes, mon, v) in valores)
            d[(aut, c, anio, mes, mon)] = v;
        return d;
    }

    private static BloqueAutorizanteVM Bloque(int autId, TipoAutorizante tipo,
        params (ConceptoPlanMonto, int?, int?, Moneda, decimal)[] guardados)
    {
        var b = new BloqueAutorizanteVM { AutorizanteId = autId, Tipo = tipo };
        foreach (var (c, anio, mes, mon, monto) in guardados)
            b.Valores[(c, anio, mes, mon)] = monto;
        return b;
    }

    [Fact]
    public void SumaPlanificado_CombinaAnticipoPeriodosVivosYCurvaFueraDeHorizonte()
    {
        var celdas = Celdas(
            (1, ConceptoPlanMonto.AnticipoFinanciero, null, null, Moneda.Pesos, 100m),
            (1, ConceptoPlanMonto.Mensual, 2030, 1, Moneda.Pesos, 50m),
            (1, ConceptoPlanMonto.CalculoAnual, 2031, null, Moneda.Pesos, 25m));
        // La curva guardada tiene un mes dentro del horizonte (NO debe volver a sumarse:
        // ya está representado por su celda en vivo) y otro fuera (SÍ debe sumarse).
        var bloque = Bloque(1, TipoAutorizante.Basica,
            (ConceptoPlanMonto.Mensual, 2030, 1, Moneda.Pesos, 40m),
            (ConceptoPlanMonto.Mensual, 2035, 6, Moneda.Pesos, 999m));

        var t = new TotalesPlan(celdas, [bloque], Periodos);

        Assert.Equal(100m + 50m + 25m + 999m, t.SumaPlanificado(1, Moneda.Pesos));
    }

    [Fact]
    public void SumaPlanificado_CeldaVacia_NoRompeElResto()
    {
        // La celda vacía cuenta 0 pero no anula las demás (un assert de "todo 0" pasaría
        // aunque SumaPlanificado devolviera 0 siempre).
        var celdas = Celdas(
            (1, ConceptoPlanMonto.Mensual, 2030, 1, Moneda.Pesos, null),
            (1, ConceptoPlanMonto.Mensual, 2030, 2, Moneda.Pesos, 60m));
        var t = new TotalesPlan(celdas, [Bloque(1, TipoAutorizante.Basica)], Periodos);

        Assert.Equal(60m, t.SumaPlanificado(1, Moneda.Pesos));
    }

    [Fact]
    public void SumaPlanificado_BucketAnualRenderizado_NoSeDuplica()
    {
        // El bucket anual tiene Mes = null en la clave: la comparación contra los
        // períodos renderizados debe reconocerlo igual que un mes normal.
        var celdas = Celdas((1, ConceptoPlanMonto.CalculoAnual, 2031, null, Moneda.Pesos, 25m));
        var bloque = Bloque(1, TipoAutorizante.Basica,
            (ConceptoPlanMonto.CalculoAnual, 2031, null, Moneda.Pesos, 20m)); // mismo bucket, ya en vivo

        var t = new TotalesPlan(celdas, [bloque], Periodos);

        Assert.Equal(25m, t.SumaPlanificado(1, Moneda.Pesos));
    }

    [Fact]
    public void SumaPlanificado_CurvaAnualFueraDeHorizonte_SeSuma()
    {
        var bloque = Bloque(1, TipoAutorizante.Basica,
            (ConceptoPlanMonto.CalculoAnual, 2040, null, Moneda.Pesos, 80m));
        var t = new TotalesPlan(Celdas(), [bloque], Periodos);

        Assert.Equal(80m, t.SumaPlanificado(1, Moneda.Pesos));
    }

    [Fact]
    public void SumaPlanificado_AutorizanteSinBloque_DevuelveSoloLoEnVivo()
    {
        var celdas = Celdas((9, ConceptoPlanMonto.Mensual, 2030, 1, Moneda.Pesos, 10m));
        var t = new TotalesPlan(celdas, [Bloque(1, TipoAutorizante.Basica)], Periodos);

        Assert.Equal(10m, t.SumaPlanificado(9, Moneda.Pesos));
    }

    [Fact]
    public void SumaPlanificado_FiltraPorMoneda()
    {
        var celdas = Celdas(
            (1, ConceptoPlanMonto.Mensual, 2030, 1, Moneda.Pesos, 50m),
            (1, ConceptoPlanMonto.Mensual, 2030, 1, Moneda.USD, 7m));
        var t = new TotalesPlan(celdas, [Bloque(1, TipoAutorizante.Basica)], Periodos);

        Assert.Equal(50m, t.SumaPlanificado(1, Moneda.Pesos));
        Assert.Equal(7m, t.SumaPlanificado(1, Moneda.USD));
    }

    [Fact]
    public void AutorizadoVsPlanificado_EsLaDiferencia()
    {
        var celdas = Celdas(
            (1, ConceptoPlanMonto.MontoAutorizado, null, null, Moneda.Pesos, 1000m),
            (1, ConceptoPlanMonto.Mensual, 2030, 1, Moneda.Pesos, 300m));
        var t = new TotalesPlan(celdas, [Bloque(1, TipoAutorizante.Adicional)], Periodos);

        Assert.Equal(1000m, t.MontoAutorizado(1, Moneda.Pesos));
        Assert.Equal(700m, t.AutorizadoVsPlanificado(1, Moneda.Pesos));
    }

    [Fact]
    public void MontoAutorizado_ObraBasica_EsElPresupuestoDelBloqueNoLaCelda()
    {
        // La básica no tiene celda de Monto Autorizado: su autorizado es el presupuesto de
        // la obra, que el VM deja en los Valores del bloque. Una celda así (no debería
        // existir) se ignora, y en otra moneda que Pesos no hay autorizado.
        var celdas = Celdas(
            (1, ConceptoPlanMonto.MontoAutorizado, null, null, Moneda.Pesos, 5000m),
            (1, ConceptoPlanMonto.Mensual, 2030, 1, Moneda.Pesos, 300m));
        var basica = Bloque(1, TipoAutorizante.Basica,
            (ConceptoPlanMonto.MontoAutorizado, null, null, Moneda.Pesos, 1000m));
        var t = new TotalesPlan(celdas, [basica], Periodos);

        Assert.Equal(1000m, t.MontoAutorizado(1, Moneda.Pesos));
        Assert.Equal(700m, t.AutorizadoVsPlanificado(1, Moneda.Pesos));
        Assert.Equal(0m, t.MontoAutorizado(1, Moneda.USD));
    }

    [Fact]
    public void PlanificadoPorTipo_SoloSumaLosBloquesDelTipo()
    {
        var celdas = Celdas(
            (1, ConceptoPlanMonto.Mensual, 2030, 1, Moneda.Pesos, 100m),
            (2, ConceptoPlanMonto.Mensual, 2030, 1, Moneda.Pesos, 30m),
            (3, ConceptoPlanMonto.Mensual, 2030, 1, Moneda.Pesos, 5m));
        var bloques = new[]
        {
            Bloque(1, TipoAutorizante.Basica),
            Bloque(2, TipoAutorizante.Adicional),
            Bloque(3, TipoAutorizante.Adicional),
        };
        var t = new TotalesPlan(celdas, bloques, Periodos);

        Assert.Equal(100m, t.PlanificadoPorTipo(TipoAutorizante.Basica, Moneda.Pesos));
        Assert.Equal(35m, t.PlanificadoPorTipo(TipoAutorizante.Adicional, Moneda.Pesos));
        Assert.Equal(0m, t.PlanificadoPorTipo(TipoAutorizante.BED, Moneda.Pesos));
    }

    [Fact]
    public void Agregados_IncluyenLaCurvaFueraDeHorizonte()
    {
        // Los agregados deben pasar por SumaPlanificado (que suma la curva guardada fuera
        // del horizonte), no leer `celdas` por su cuenta: si no, el pie de la grilla
        // subestimaría lo que el export y el snapshot sí incluyen.
        var celdas = Celdas(
            (1, ConceptoPlanMonto.Mensual, 2030, 1, Moneda.Pesos, 100m),
            (2, ConceptoPlanMonto.Mensual, 2030, 1, Moneda.Pesos, 10m));
        var bloques = new[]
        {
            Bloque(1, TipoAutorizante.Basica, (ConceptoPlanMonto.Mensual, 2035, 6, Moneda.Pesos, 500m)),
            Bloque(2, TipoAutorizante.Adicional, (ConceptoPlanMonto.CalculoAnual, 2040, null, Moneda.Pesos, 7m)),
        };
        var t = new TotalesPlan(celdas, bloques, Periodos);

        Assert.Equal(600m, t.PlanificadoPorTipo(TipoAutorizante.Basica, Moneda.Pesos));
        Assert.Equal(17m, t.PlanificadoPorTipo(TipoAutorizante.Adicional, Moneda.Pesos));
        Assert.Equal(617m, t.TotalPlanificado(Moneda.Pesos));
    }

    [Fact]
    public void AutorizadoVsPlanificado_Sobreplanificado_EsNegativo()
    {
        // La grilla pinta en rojo cuando la diferencia es < 0.
        var celdas = Celdas((1, ConceptoPlanMonto.Mensual, 2030, 1, Moneda.Pesos, 250m));
        var basica = Bloque(1, TipoAutorizante.Basica,
            (ConceptoPlanMonto.MontoAutorizado, null, null, Moneda.Pesos, 100m)); // presupuesto de la obra
        var t = new TotalesPlan(celdas, [basica], Periodos);

        Assert.Equal(-150m, t.AutorizadoVsPlanificado(1, Moneda.Pesos));
    }

    [Fact]
    public void Totales_SumanTodosLosBloques()
    {
        var celdas = Celdas(
            (1, ConceptoPlanMonto.Mensual, 2030, 1, Moneda.Pesos, 100m),
            (2, ConceptoPlanMonto.MontoAutorizado, null, null, Moneda.Pesos, 500m),
            (2, ConceptoPlanMonto.AnticipoFinanciero, null, null, Moneda.Pesos, 30m));
        var bloques = new[]
        {
            // El autorizado de la básica es el presupuesto de la obra (Valores del bloque).
            Bloque(1, TipoAutorizante.Basica, (ConceptoPlanMonto.MontoAutorizado, null, null, Moneda.Pesos, 1000m)),
            Bloque(2, TipoAutorizante.BED),
        };
        var t = new TotalesPlan(celdas, bloques, Periodos);

        Assert.Equal(130m, t.TotalPlanificado(Moneda.Pesos));
        Assert.Equal(1500m, t.TotalAutorizado(Moneda.Pesos));
    }
}
