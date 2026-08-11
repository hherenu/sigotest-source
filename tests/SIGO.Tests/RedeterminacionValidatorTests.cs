using SIGO.Models.Enums;
using SIGO.Services.Validaciones;

namespace SIGO.Tests;

/// <summary>
/// Reglas de cálculo y disparos de redeterminación, puras en RedeterminacionValidator.
/// </summary>
public class RedeterminacionValidatorTests
{
    [Fact]
    public void Calcular_ConPesosQueSuman100_Pasa() =>
        Assert.Null(RedeterminacionValidator.Calcular(100m));

    [Fact]
    public void Calcular_ConPesosDesviados_Rechaza()
    {
        var error = RedeterminacionValidator.Calcular(99.5m);
        Assert.Contains("debe sumar 100%", error);
    }

    [Fact]
    public void GuardarDisparo_SinCoeficienteTotal_Rechaza()
    {
        Assert.NotNull(RedeterminacionValidator.GuardarDisparo(null));
        Assert.Null(RedeterminacionValidator.GuardarDisparo(1.0432m));
    }

    [Fact]
    public void MismaObra_DisparoDeOtraObra_Rechaza()
    {
        Assert.Null(RedeterminacionValidator.MismaObra(5, 5));
        Assert.Contains("otra obra", RedeterminacionValidator.MismaObra(5, 6));
    }

    [Fact]
    public void TablaDeLaObra_AjenaOInexistente_Rechaza()
    {
        Assert.Null(RedeterminacionValidator.TablaDeLaObra(5, 5));
        Assert.Contains("no pertenece", RedeterminacionValidator.TablaDeLaObra(6, 5));
        Assert.Contains("no pertenece", RedeterminacionValidator.TablaDeLaObra(null, 5));
    }

    [Fact]
    public void RecalcularDisparo_SinCoeficienteTotal_Rechaza()
    {
        Assert.NotNull(RedeterminacionValidator.RecalcularDisparo(null));
        Assert.Null(RedeterminacionValidator.RecalcularDisparo(1.0432m));
    }

    [Theory]
    [InlineData(EstadoRedeterminacion.Borrador, true)]
    [InlineData(EstadoRedeterminacion.Calculada, true)]
    [InlineData(EstadoRedeterminacion.Presentada, false)]
    [InlineData(EstadoRedeterminacion.AprobadaCCyR, false)]
    [InlineData(EstadoRedeterminacion.AprobadaOS, false)]
    public void EsRecalculable_SoloBorradorYCalculada(EstadoRedeterminacion estado, bool esperado) =>
        Assert.Equal(esperado, RedeterminacionValidator.EsRecalculable(estado));

    [Fact]
    public void AdmiteRecalculo_EnEstadoOficial_RechazaConElEstado()
    {
        var error = RedeterminacionValidator.AdmiteRecalculo(EstadoRedeterminacion.AprobadaCCyR, 3);
        Assert.Contains("disparo 3", error);
        Assert.Contains("AprobadaCCyR", error);
    }

    [Fact]
    public void EliminarDisparo_SoloElUltimoYRecalculable()
    {
        Assert.Null(RedeterminacionValidator.EliminarDisparo(EstadoRedeterminacion.Calculada, 3, hayPosterior: false));

        // Los posteriores acumulan su coeficiente: eliminar uno intermedio rompe la cadena.
        Assert.Contains("el último disparo",
            RedeterminacionValidator.EliminarDisparo(EstadoRedeterminacion.Calculada, 2, hayPosterior: true));

        Assert.Contains("no puede eliminarse",
            RedeterminacionValidator.EliminarDisparo(EstadoRedeterminacion.Presentada, 3, hayPosterior: false));
    }
}
