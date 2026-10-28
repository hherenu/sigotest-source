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
    [InlineData(EstadoRedeterminacion.Rechazada, false)]
    [InlineData(EstadoRedeterminacion.Anulada, false)]
    public void EsRecalculable_SoloBorradorYCalculada(EstadoRedeterminacion estado, bool esperado) =>
        Assert.Equal(esperado, RedeterminacionValidator.EsRecalculable(estado));

    [Fact]
    public void AdmiteRecalculo_EnEstadoOficial_RechazaConElEstadoYComoCorregirlo()
    {
        var error = RedeterminacionValidator.AdmiteRecalculo(EstadoRedeterminacion.AprobadaCCyR, 3, hayPosterior: false);
        Assert.Contains("disparo 3", error);
        Assert.Contains("«Aprobada CCyR»", error);
        Assert.Contains("datos administrativos", error);
    }

    // ── Estado del trámite según los datos administrativos ──────────────────────

    [Theory]
    [InlineData(null, null, false, false, EstadoRedeterminacion.Calculada)]
    [InlineData(null, "  ", false, false, EstadoRedeterminacion.Calculada)]
    [InlineData(null, "EX-2026-1-GCABA-SBASE", false, false, EstadoRedeterminacion.Presentada)]
    [InlineData(null, "EX-2026-1-GCABA-SBASE", true, false, EstadoRedeterminacion.AprobadaCCyR)]
    [InlineData(null, null, true, true, EstadoRedeterminacion.AprobadaOS)] // el historial importado trae fechas sin expediente
    [InlineData(EstadoRedeterminacion.Anulada, "EX-2026-1-GCABA-SBASE", true, true, EstadoRedeterminacion.Anulada)]
    [InlineData(EstadoRedeterminacion.Rechazada, "EX-2026-1-GCABA-SBASE", false, false, EstadoRedeterminacion.Rechazada)]
    public void EstadoAdministrativo_LaBajaMandaYDespuesLoMasAvanzado(
        EstadoRedeterminacion? baja, string? expediente, bool conCCyR, bool conOS, EstadoRedeterminacion esperado) =>
        Assert.Equal(esperado, RedeterminacionValidator.EstadoAdministrativo(
            baja, expediente, conCCyR ? new DateTime(2026, 5, 1) : null, conOS ? new DateTime(2026, 6, 1) : null));

    [Theory]
    [InlineData(EstadoRedeterminacion.Presentada, true)]
    [InlineData(EstadoRedeterminacion.AprobadaOS, true)]
    [InlineData(EstadoRedeterminacion.Rechazada, false)]
    [InlineData(EstadoRedeterminacion.Anulada, false)]
    public void EsVigente_SalvoRechazadaOAnulada(EstadoRedeterminacion estado, bool esperado) =>
        Assert.Equal(esperado, RedeterminacionValidator.EsVigente(estado));

    [Fact]
    public void CambioDeVigencia_SoloSeDaDeBajaElUltimoVigente()
    {
        Assert.Null(RedeterminacionValidator.CambioDeVigencia(
            EstadoRedeterminacion.Presentada, EstadoRedeterminacion.Anulada, 3, hayVigentePosterior: false, hayOtroVigenteConElMismoNumero: false));
        Assert.Contains("Solo se puede rechazar el último disparo vigente", RedeterminacionValidator.CambioDeVigencia(
            EstadoRedeterminacion.Calculada, EstadoRedeterminacion.Rechazada, 2, hayVigentePosterior: true, hayOtroVigenteConElMismoNumero: false));
    }

    [Fact]
    public void CambioDeVigencia_VuelveAEstarVigenteSoloConSuNumeroLibreYSinPosteriores()
    {
        Assert.Null(RedeterminacionValidator.CambioDeVigencia(
            EstadoRedeterminacion.Anulada, EstadoRedeterminacion.Calculada, 3, hayVigentePosterior: false, hayOtroVigenteConElMismoNumero: false));
        Assert.Contains("otro disparo vigente con el número 3", RedeterminacionValidator.CambioDeVigencia(
            EstadoRedeterminacion.Anulada, EstadoRedeterminacion.Calculada, 3, hayVigentePosterior: false, hayOtroVigenteConElMismoNumero: true));
        Assert.Contains("disparos vigentes posteriores", RedeterminacionValidator.CambioDeVigencia(
            EstadoRedeterminacion.Rechazada, EstadoRedeterminacion.Presentada, 3, hayVigentePosterior: true, hayOtroVigenteConElMismoNumero: false));
        // Entre bajas, o entre estados vigentes, la numeración no cambia.
        Assert.Null(RedeterminacionValidator.CambioDeVigencia(
            EstadoRedeterminacion.Rechazada, EstadoRedeterminacion.Anulada, 3, hayVigentePosterior: true, hayOtroVigenteConElMismoNumero: true));
    }

    [Fact]
    public void AdmiteRecalculo_SoloElUltimo()
    {
        Assert.Null(RedeterminacionValidator.AdmiteRecalculo(EstadoRedeterminacion.Calculada, 3, hayPosterior: false));

        // Recalcular uno intermedio reescribiría la VariacionAcumulada de los
        // posteriores (incluidos los oficiales): misma regla que EliminarDisparo.
        Assert.Contains("el último disparo",
            RedeterminacionValidator.AdmiteRecalculo(EstadoRedeterminacion.Calculada, 2, hayPosterior: true));
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

    [Theory]
    [InlineData(2026, 12, 2026, 6)]   // salto antes de la base: coeficiente invertido
    [InlineData(2026, 6, 2026, 6)]    // el mismo mes: coeficiente 1
    [InlineData(2026, 1, 2025, 12)]
    public void Periodos_SaltoQueNoEsPosteriorALaBase_Rechaza(int anioBase, int mesBase, int anioSalto, int mesSalto) =>
        Assert.Equal($"El mes de salto ({mesSalto:00}/{anioSalto}) tiene que ser posterior al mes base ({mesBase:00}/{anioBase}).",
            RedeterminacionValidator.Periodos(anioBase, mesBase, anioSalto, mesSalto));

    [Fact]
    public void Periodos_SaltoPosterior_Pasa()
    {
        Assert.Null(RedeterminacionValidator.Periodos(2026, 6, 2026, 7));
        Assert.Null(RedeterminacionValidator.Periodos(2025, 12, 2026, 1));
    }
}
