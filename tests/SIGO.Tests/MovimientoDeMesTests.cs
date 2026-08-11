using SIGO.Services;

namespace SIGO.Tests;

/// <summary>
/// La fórmula del movimiento del mes es la ÚNICA implementación compartida entre
/// GuardarItemsAsync (lo que se guarda) y LiveMonto (lo que se muestra): estos tests
/// fijan la base de cálculo en cascada y los redondeos intermedios a 4 decimales.
/// </summary>
public class MovimientoDeMesTests
{
    [Fact]
    public void ConCantidadYPU_CalculaPorCantidad()
    {
        var (cantidad, monto) = CertificadoService.MovimientoDeMes(
            cantidadContrato: 100m, puBasico: 10m, montoContrato: 1000m, pct: 10m);

        Assert.Equal(10m, cantidad);
        Assert.Equal(100m, monto);
    }

    [Fact]
    public void SinCantidad_CaeAlMontoDeContrato()
    {
        // Ítems globales ("1 gl"): el monto sale directo del % sobre el contrato.
        var (cantidad, monto) = CertificadoService.MovimientoDeMes(
            cantidadContrato: null, puBasico: null, montoContrato: 5000m, pct: 20m);

        Assert.Equal(0m, cantidad);
        Assert.Equal(1000m, monto);
    }

    [Fact]
    public void ConPUPeroSinCantidad_TambienCaeAlMonto()
    {
        // La base exige AMBOS (cantidad y PU): con PU suelto se usa el monto.
        var (_, monto) = CertificadoService.MovimientoDeMes(
            cantidadContrato: null, puBasico: 10m, montoContrato: 5000m, pct: 10m);

        Assert.Equal(500m, monto);
    }

    [Fact]
    public void SinNingunaBase_DaCero()
    {
        var (cantidad, monto) = CertificadoService.MovimientoDeMes(null, null, null, 50m);

        Assert.Equal(0m, cantidad);
        Assert.Equal(0m, monto);
    }

    [Fact]
    public void RedondeaLaCantidadAntesDeMultiplicar()
    {
        // Doble redondeo documentado: la cantidad se corta a 4 decimales ANTES de
        // multiplicar por el PU (0.3333 × 3 = 0.9999, no 1.0000). Si esta regla
        // cambia, deben cambiar las dos puntas a la vez (guardado y LiveMonto).
        var (cantidad, monto) = CertificadoService.MovimientoDeMes(
            cantidadContrato: 1m, puBasico: 3m, montoContrato: 3m, pct: 33.3333m);

        Assert.Equal(0.3333m, cantidad);
        Assert.Equal(0.9999m, monto);
    }
}
