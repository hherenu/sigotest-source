using SIGO.Models.Enums;
using SIGO.Services.Validaciones;

namespace SIGO.Tests;

/// <summary>
/// Máquina de estados del circuito de planificación, pura en PlanificacionValidator.
/// </summary>
public class PlanificacionValidatorTests
{
    [Fact]
    public void AgregarBloque_SegundaBasica_Rechaza()
    {
        Assert.Equal("La obra básica ya existe en el plan.",
            PlanificacionValidator.AgregarBloque(TipoAutorizante.Basica, yaTieneBasica: true));
        Assert.Null(PlanificacionValidator.AgregarBloque(TipoAutorizante.Basica, yaTieneBasica: false));
        // Adicionales/BED no tienen tope: la unicidad de la básica no los alcanza.
        Assert.Null(PlanificacionValidator.AgregarBloque(TipoAutorizante.Adicional, yaTieneBasica: true));
    }

    [Theory]
    [InlineData(EstadoPlanificacion.Pendiente, true)]
    [InlineData(EstadoPlanificacion.EnRevision, true)]
    [InlineData(EstadoPlanificacion.Cargada, false)]
    [InlineData(EstadoPlanificacion.Aprobada, false)]
    public void MarcarCargada_SoloDesdePendienteOEnRevision(EstadoPlanificacion estado, bool pasa) =>
        Assert.Equal(pasa, PlanificacionValidator.MarcarCargada(estado, cantidadBloques: 1) is null);

    [Fact]
    public void MarcarCargada_SinBloques_Rechaza() =>
        Assert.Equal("El plan no tiene bloques cargados.",
            PlanificacionValidator.MarcarCargada(EstadoPlanificacion.Pendiente, cantidadBloques: 0));

    [Theory]
    [InlineData(EstadoPlanificacion.Cargada, true)]
    [InlineData(EstadoPlanificacion.Aprobada, true)]  // salida de una aprobación por error
    [InlineData(EstadoPlanificacion.Pendiente, false)]
    [InlineData(EstadoPlanificacion.EnRevision, false)]
    public void EnviarARevision_SoloDesdeCargadaOAprobada(EstadoPlanificacion estado, bool pasa) =>
        Assert.Equal(pasa, PlanificacionValidator.EnviarARevision(estado, "motivo válido") is null);

    // Espejo server del RequiredValidator del form: Radzen deja pasar los espacios.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EnviarARevision_SinMotivo_Rechaza(string? motivo) =>
        Assert.Contains("motivo", PlanificacionValidator.EnviarARevision(EstadoPlanificacion.Cargada, motivo));

    [Fact]
    public void Aprobar_SoloDesdeCargada()
    {
        Assert.Null(PlanificacionValidator.Aprobar(EstadoPlanificacion.Cargada));
        Assert.NotNull(PlanificacionValidator.Aprobar(EstadoPlanificacion.Pendiente));
    }

    [Fact]
    public void TomarConocimiento_SoloAprobadaYUnaVezPorCiclo()
    {
        Assert.Null(PlanificacionValidator.TomarConocimiento(EstadoPlanificacion.Aprobada, fechaTomaConocimiento: null));

        Assert.Equal("Solo se puede tomar conocimiento de un plan Aprobado.",
            PlanificacionValidator.TomarConocimiento(EstadoPlanificacion.Cargada, null));

        Assert.Equal("La toma de conocimiento de este ciclo ya fue registrada.",
            PlanificacionValidator.TomarConocimiento(EstadoPlanificacion.Aprobada, DateTime.UtcNow));
    }
}
