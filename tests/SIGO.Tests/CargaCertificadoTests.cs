using SIGO.Models.ViewModels;

namespace SIGO.Tests;

/// <summary>
/// Estado de la grilla de carga al recargar el certificado (agregar o quitar un bloque):
/// se conserva solo lo que el usuario tipeó; lo demás sale de la versión recién leída, que
/// puede traer lo que otro usuario guardó en el medio.
/// </summary>
public class CargaCertificadoTests
{
    private const int Bloque = 10;
    private const int ItemA = 1, ItemB = 2, ItemC = 3, Rubro = 9;

    /// <summary>Certificado con un bloque: un rubro y tres hojas con el % del mes guardado.</summary>
    private static CertificadoVM Vm(decimal? pctA, decimal? pctB, decimal? pctC, int bloque = Bloque) => new()
    {
        Bloques =
        [
            new BloqueCertificadoVM
            {
                CertificadoEstructuraId = bloque,
                Items =
                [
                    new ItemCertificadoVM { ItemEstructuraId = Rubro, EsAgrupador = true },
                    new ItemCertificadoVM { ItemEstructuraId = ItemA, AgrupadorPadreId = Rubro, PorcentajeMes = pctA },
                    new ItemCertificadoVM { ItemEstructuraId = ItemB, AgrupadorPadreId = Rubro, PorcentajeMes = pctB },
                    new ItemCertificadoVM { ItemEstructuraId = ItemC, AgrupadorPadreId = Rubro, PorcentajeMes = pctC }
                ]
            }
        ]
    };

    [Fact]
    public void PrimeraCarga_TomaLoGuardadoYLosCerosQuedanVacios()
    {
        var pct = CargaCertificado.Porcentajes(Vm(10m, 0m, null));

        Assert.Equal(new Dictionary<int, decimal?> { [ItemA] = 10m, [ItemB] = null, [ItemC] = null }, pct[Bloque]);
    }

    [Fact]
    public void Recarga_ConservaLoTipeadoYTomaLoQueOtroGuardoEnLoDemas()
    {
        var anterior = Vm(10m, 20m, null);
        var tipeado = CargaCertificado.Porcentajes(anterior);
        tipeado[Bloque][ItemA] = 15m;            // el usuario cambia A
        tipeado[Bloque][ItemC] = null;           // y deja C como estaba (vacío)

        // Otro usuario guardó en el medio: B pasó a 25 y C a 5.
        var recargado = Vm(10m, 25m, 5m);

        var pct = CargaCertificado.Porcentajes(recargado, anterior, tipeado);

        Assert.Equal(15m, pct[Bloque][ItemA]);   // lo tipeado sobrevive a la recarga
        Assert.Equal(25m, pct[Bloque][ItemB]);   // antes se reponía el 20 viejo y el próximo
        Assert.Equal(5m, pct[Bloque][ItemC]);    // guardado pisaba lo del otro usuario
    }

    [Fact]
    public void Recarga_SiElUsuarioTipeoElMismoItemQueOtroGuardo_GanaLoTipeado()
    {
        // Lo que el usuario cambió a propósito no se descarta: el próximo guardado lo aplica
        // (es su carga), aunque otro usuario haya guardado otro valor en el medio.
        var anterior = Vm(10m, null, null);
        var tipeado = CargaCertificado.Porcentajes(anterior);
        tipeado[Bloque][ItemA] = 15m;

        var pct = CargaCertificado.Porcentajes(Vm(12m, null, null), anterior, tipeado);

        Assert.Equal(15m, pct[Bloque][ItemA]);
    }

    [Fact]
    public void Recarga_BorrarUnValorTambienEsCambio()
    {
        var anterior = Vm(10m, null, null);
        var tipeado = CargaCertificado.Porcentajes(anterior);
        tipeado[Bloque][ItemA] = null;

        var pct = CargaCertificado.Porcentajes(Vm(10m, null, null), anterior, tipeado);

        Assert.Null(pct[Bloque][ItemA]);
    }

    [Fact]
    public void Recarga_BloqueNuevoSaleDeLaBaseYElQuitadoDesaparece()
    {
        var anterior = Vm(10m, null, null);
        var tipeado = CargaCertificado.Porcentajes(anterior);
        tipeado[Bloque][ItemA] = 30m;

        // El bloque 10 se quitó y apareció el 20 (mismos ítems, otro bloque).
        var pct = CargaCertificado.Porcentajes(Vm(7m, null, null, bloque: 20), anterior, tipeado);

        Assert.False(pct.ContainsKey(Bloque));
        Assert.Equal(7m, pct[20][ItemA]);
    }
}
