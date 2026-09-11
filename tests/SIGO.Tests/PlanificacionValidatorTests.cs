using SIGO.Models;
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

    // ── Presupuesto de la obra y tope del 50% para adicionales/BED ─────────────

    [Fact]
    public void ObraEnPlanificacion_SoloRechazaProyectada()
    {
        Assert.Contains("Proyectada", PlanificacionValidator.ObraEnPlanificacion("Proyectada"));
        Assert.Null(PlanificacionValidator.ObraEnPlanificacion("Vigente"));
        Assert.Null(PlanificacionValidator.ObraEnPlanificacion("Proceso Licitatorio"));
    }

    [Fact]
    public void PresupuestoObra_SinPresupuesto_Rechaza()
    {
        Assert.Contains("no tiene presupuesto oficial",
            PlanificacionValidator.PresupuestoObra(new PresupuestosObra(0m, null, null, null, null, null)));
        Assert.Null(PlanificacionValidator.PresupuestoObra(new PresupuestosObra(1m, null, null, null, null, null)));
    }

    [Fact]
    public void AdicionalSobreLimite_AvisaSoloPorEncimaDel50PorCiento()
    {
        // Hasta el 50% inclusive no avisa; por encima, avisa con el bloque, la moneda, el importe y el tope.
        Assert.Null(PlanificacionValidator.AdicionalSobreLimite("Adicional N°1", Moneda.Pesos, 500m, 1000m, "Presupuesto oficial"));
        var aviso = PlanificacionValidator.AdicionalSobreLimite("Adicional N°1", Moneda.USD, 500.01m, 1000m, "Presupuesto adjudicado");
        Assert.Contains("Adicional N°1 en USD", aviso);
        Assert.Contains("supera el 50% del presupuesto adjudicado", aviso);
    }

    [Fact]
    public void AdicionalSobreLimite_SinPresupuestoEnLaMoneda_NoCompara() =>
        Assert.Null(PlanificacionValidator.AdicionalSobreLimite("BED N°1", Moneda.EUR, 999m, 0m, "Presupuesto oficial"));

    // ── Mes del anticipo ≤ primer mes del plan ─────────────────────────────────

    [Theory]
    [InlineData(2029, 12)] // anterior: el anticipo suele pagarse antes del inicio
    [InlineData(2030, 3)]  // el mismo primer mes
    public void MesAnticipo_HastaElPrimerMes_Pasa(int anio, int mes) =>
        Assert.Null(PlanificacionValidator.MesAnticipo([("Obra Básica", anio, mes)], primerAnio: 2030, primerMes: 3));

    [Fact]
    public void MesAnticipo_SinMes_NoValida() =>
        // Datos anteriores a la mejora (anticipo sin mes) no bloquean.
        Assert.Null(PlanificacionValidator.MesAnticipo([("Obra Básica", null, null)], primerAnio: 2030, primerMes: 3));

    [Theory]
    [InlineData(2030, 4)]  // mes siguiente
    [InlineData(2031, 1)]  // año siguiente con mes menor: se compara (año, mes), no el mes suelto
    public void MesAnticipo_Posterior_Rechaza(int anio, int mes)
    {
        var error = PlanificacionValidator.MesAnticipo([("Obra Básica", anio, mes)], primerAnio: 2030, primerMes: 3);
        Assert.Equal(
            $"El mes del anticipo no puede ser posterior al primer mes del plan (03/2030). Obra Básica: {mes:00}/{anio}.",
            error);
    }

    [Fact]
    public void MesAnticipo_EnumeraSoloLosBloquesTardios()
    {
        var error = PlanificacionValidator.MesAnticipo(
            [("Obra Básica", 2030, 3), ("Adicional N°1", 2030, 5), ("BED N°1", 2030, 6)],
            primerAnio: 2030, primerMes: 3);
        Assert.Contains("Adicional N°1: 05/2030; BED N°1: 06/2030.", error);
        Assert.DoesNotContain("Obra Básica", error);
    }
}
