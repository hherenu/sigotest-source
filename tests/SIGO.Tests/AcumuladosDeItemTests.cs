using SIGO.Models;
using SIGO.Services;

namespace SIGO.Tests;

/// <summary>
/// La regla de acumulados previos (sumas + cascada de base cantidad → monto → % directo)
/// es la única implementación compartida entre BuildVmAsync y CerrarAsync: estos tests
/// la fijan para que no vuelva a derivar entre ambos puntos.
/// </summary>
public class AcumuladosDeItemTests
{
    private static ItemCertificado Fila(int itemId, decimal cant, decimal monto, decimal pct) =>
        new() { ItemEstructuraId = itemId, CantidadActual = cant, MontoActual = monto, PorcentajeActual = pct };

    [Fact]
    public void ConBaseDeCantidad_SumaYCalculaPorCantidad()
    {
        var item = new ItemEstructura { Id = 1, Descripcion = "x", Cantidad = 100m, PUBasico = 10m, Monto = 1000m };
        var anteriores = new[] { Fila(1, 10m, 100m, 10m), Fila(1, 5m, 50m, 5m) };

        var acum = CertificadoService.AcumuladosDeItem(anteriores, item, cantMes: 5m, montoMes: 50m);

        Assert.Equal(15m, acum.CantidadAnterior);
        Assert.Equal(150m, acum.MontoAnterior);
        Assert.Equal(15m, acum.PctAnteriorDirecto);
        Assert.Equal(15m, acum.PctAnteriorPorBase);   // 15 de 100 unidades
        Assert.Equal(20m, acum.PctAcumuladoPorBase);  // + 5 del mes
    }

    [Fact]
    public void SinCantidad_CalculaPorMontoDeContrato()
    {
        var item = new ItemEstructura { Id = 2, Descripcion = "x", Monto = 1000m };
        var anteriores = new[] { Fila(2, 0m, 100m, 10m) };

        var acum = CertificadoService.AcumuladosDeItem(anteriores, item, cantMes: 0m, montoMes: 100m);

        Assert.Equal(10m, acum.PctAnteriorPorBase);   // 100 de $1.000
        Assert.Equal(20m, acum.PctAcumuladoPorBase);
    }

    [Fact]
    public void SinNingunaBase_SoloQuedaElPorcentajeDirecto()
    {
        var item = new ItemEstructura { Id = 3, Descripcion = "x" };
        var anteriores = new[] { Fila(3, 0m, 0m, 7.5m), Fila(3, 0m, 0m, 2.5m) };

        var acum = CertificadoService.AcumuladosDeItem(anteriores, item, cantMes: 0m, montoMes: 0m);

        Assert.Null(acum.PctAnteriorPorBase);         // el punto de uso decide el fallback
        Assert.Null(acum.PctAcumuladoPorBase);
        Assert.Equal(10m, acum.PctAnteriorDirecto);
    }

    [Fact]
    public void IgnoraLasFilasDeOtrosItems()
    {
        var item = new ItemEstructura { Id = 4, Descripcion = "x", Cantidad = 100m, Monto = 1000m };
        var anteriores = new[] { Fila(4, 10m, 100m, 10m), Fila(99, 50m, 500m, 50m) };

        var acum = CertificadoService.AcumuladosDeItem(anteriores, item, cantMes: 0m, montoMes: 0m);

        Assert.Equal(10m, acum.CantidadAnterior);
        Assert.Equal(100m, acum.MontoAnterior);
    }
}
