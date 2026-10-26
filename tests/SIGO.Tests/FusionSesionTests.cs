using SIGO.Models.ViewModels;

namespace SIGO.Tests;

/// <summary>
/// Qué conserva la grilla de planificación de lo tipeado al releer el plan de la base
/// (FusionSesion): solo lo que la sesión cambió y nadie más tocó. Antes se conservaba
/// todo, y tras un conflicto un segundo Guardar pisaba lo que había guardado otro usuario.
/// </summary>
public class FusionSesionTests
{
    private static (Dictionary<string, decimal> Conservar, int Pisadas) Fusionar(
        Dictionary<string, decimal> sesion, Dictionary<string, decimal> antes, Dictionary<string, decimal> ahora) =>
        FusionSesion.Fusionar(sesion, k => antes.GetValueOrDefault(k), k => ahora.GetValueOrDefault(k));

    [Fact]
    public void LoQueLaSesionNoCambio_SeReleeDeLaBase()
    {
        var (conservar, pisadas) = Fusionar(new() { ["ene"] = 100m }, new() { ["ene"] = 100m }, new() { ["ene"] = 200m });

        Assert.Empty(conservar);
        Assert.Equal(0, pisadas);
    }

    [Fact]
    public void LoTipeadoQueNadieTocoEnLaBase_SeConserva()
    {
        var (conservar, pisadas) = Fusionar(new() { ["ene"] = 150m }, new() { ["ene"] = 100m }, new() { ["ene"] = 100m });

        Assert.Equal(150m, conservar["ene"]);
        Assert.Equal(0, pisadas);
    }

    [Fact]
    public void LoTipeadoQueOtroCambioEnLaBase_MandaLaBaseYSeAvisa()
    {
        var (conservar, pisadas) = Fusionar(new() { ["ene"] = 150m }, new() { ["ene"] = 100m }, new() { ["ene"] = 200m });

        Assert.Empty(conservar);
        Assert.Equal(1, pisadas);
    }

    [Fact]
    public void ElMismoValorEnLaSesionYEnLaBase_NoEsPisada()
    {
        var (conservar, pisadas) = Fusionar(new() { ["ene"] = 150m }, new() { ["ene"] = 100m }, new() { ["ene"] = 150m });

        Assert.Empty(conservar);
        Assert.Equal(0, pisadas);
    }

    [Fact]
    public void ConflictoDeGuardado_NoLlevaValoresViejosDeCeldasQueLaSesionNoToco()
    {
        // A tipeó feb y mar; B guardó ene y mar. Tras el conflicto de A, la grilla muestra
        // ene de B (A no lo tocó), conserva feb de A y avisa que mar lo cambió B.
        var antes = new Dictionary<string, decimal> { ["ene"] = 10m, ["feb"] = 20m, ["mar"] = 30m };
        var sesionA = new Dictionary<string, decimal> { ["ene"] = 10m, ["feb"] = 25m, ["mar"] = 35m };
        var guardadoPorB = new Dictionary<string, decimal> { ["ene"] = 11m, ["feb"] = 20m, ["mar"] = 31m };

        var (conservar, pisadas) = Fusionar(sesionA, antes, guardadoPorB);

        Assert.Equal(new Dictionary<string, decimal> { ["feb"] = 25m }, conservar);
        Assert.Equal(1, pisadas);
    }

    [Fact]
    public void MesDelAnticipoBorradoEnLaSesion_SeConservaComoVacio()
    {
        var mayo = new PeriodoVM(2030, 5);
        var (conservar, pisadas) = FusionSesion.Fusionar<int, PeriodoVM?>(
            [KeyValuePair.Create(7, (PeriodoVM?)null)], _ => mayo, _ => mayo);

        Assert.True(conservar.ContainsKey(7));
        Assert.Null(conservar[7]);
        Assert.Equal(0, pisadas);
    }
}
