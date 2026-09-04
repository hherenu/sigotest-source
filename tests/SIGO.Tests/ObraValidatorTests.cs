using SIGO.Models;
using SIGO.Services.Validaciones;

namespace SIGO.Tests;

/// <summary>
/// Reglas puras de la obra en ObraValidator: el estado computado por plazo, los campos
/// mínimos del alta/edición y el mensaje de dependencias que se muestra antes de que
/// la eliminación choque con las FK Restrict.
/// </summary>
public class ObraValidatorTests
{
    private static readonly DateTime Hoy = new(2026, 7, 27);

    // ── Estado ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Estado_SinFechaFinal_EsProyectada() =>
        Assert.Equal("Proyectada", ObraValidator.Estado(null, null, null, Hoy));

    [Fact]
    public void Estado_ConAntecedentesYActaFutura_EsProcesoLicitatorio() =>
        Assert.Equal("Proceso Licitatorio", ObraValidator.Estado("EX-2026-123", Hoy.AddDays(30), null, Hoy));

    [Fact]
    public void Estado_ProcesoLicitatorio_PrevaleceSobreElPlazo() =>
        // Si todavía no arrancó (acta futura) y tiene expediente, está en licitación aunque
        // ya tenga cargada una fecha final de contrato.
        Assert.Equal("Proceso Licitatorio", ObraValidator.Estado("EX-2026-123", Hoy.AddDays(30), Hoy.AddDays(400), Hoy));

    [Fact]
    public void Estado_ConAntecedentesPeroActaHoy_NoEsProcesoLicitatorio() =>
        // El acta de inicio tiene que ser estrictamente posterior a hoy.
        Assert.Equal("Proyectada", ObraValidator.Estado("EX-2026-123", Hoy, null, Hoy));

    [Fact]
    public void Estado_ConAntecedentesSinActa_EsProyectada() =>
        Assert.Equal("Proyectada", ObraValidator.Estado("EX-2026-123", null, null, Hoy));

    [Fact]
    public void Estado_ActaFuturaSinAntecedentes_EsProyectada() =>
        Assert.Equal("Proyectada", ObraValidator.Estado("  ", Hoy.AddDays(30), null, Hoy));

    [Fact]
    public void Estado_FechaFutura_EsVigente() =>
        Assert.Equal("Vigente", ObraValidator.Estado(null, null, Hoy.AddDays(1), Hoy));

    [Fact]
    public void Estado_MismoDia_SigueVigente() =>
        // El plazo vence al terminar el día: el día de la fecha final la obra aún está vigente.
        Assert.Equal("Vigente", ObraValidator.Estado(null, null, Hoy, Hoy));

    [Fact]
    public void Estado_FechaPasada_EsPlazoVencido() =>
        Assert.Equal("Plazo Vencido", ObraValidator.Estado(null, null, Hoy.AddDays(-1), Hoy));

    [Fact]
    public void Estado_ObraIniciadaConAntecedentes_UsaElPlazo() =>
        // Con acta ya pasada, los antecedentes no cambian nada: manda el plazo contractual.
        Assert.Equal("Vigente", ObraValidator.Estado("EX-2026-123", Hoy.AddDays(-10), Hoy.AddDays(100), Hoy));

    [Fact]
    public void Estado_IgnoraLaHora()
    {
        // Se compara por fecha calendario: una fecha final "hoy a las 00:00" contra un
        // "hoy" con hora sigue siendo vigente.
        var finConHora = new DateTime(2026, 7, 27, 0, 0, 0);
        var hoyConHora = new DateTime(2026, 7, 27, 23, 59, 0);
        Assert.Equal("Vigente", ObraValidator.Estado(null, null, finConHora, hoyConHora));
    }

    // ── EnPlanificacion (espejo de Estado sobre la entidad) ──────────────────────

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("EX-1", null, null)]
    [InlineData("EX-1", 30, null)]
    [InlineData("EX-1", 30, 400)]
    [InlineData("EX-1", 0, null)]
    [InlineData("  ", 30, null)]
    [InlineData(null, null, 1)]
    [InlineData(null, null, -1)]
    [InlineData("EX-1", -10, 100)]
    public void EnPlanificacion_CoincideConEstado(string? antecedentes, int? diasActa, int? diasFinal)
    {
        var obra = new Obra
        {
            Nombre = "x", NumeroLicitacion = "x", Antecedentes = antecedentes,
            FechaActaInicio = diasActa is int a ? Hoy.AddDays(a) : null,
            FechaFinalContrato = diasFinal is int f ? Hoy.AddDays(f) : null
        };
        var esperado = ObraValidator.Estado(antecedentes, obra.FechaActaInicio, obra.FechaFinalContrato, Hoy) != "Proyectada";
        Assert.Equal(esperado, ObraValidator.EnPlanificacion(Hoy).Compile()(obra));
    }

    // ── Guardar (campos mínimos) ─────────────────────────────────────────────────

    [Fact]
    public void Guardar_ConNombreYLicitacion_Pasa() =>
        Assert.Null(ObraValidator.Guardar("Estación Sáenz", "LP 123/25"));

    [Fact]
    public void Guardar_SinNombre_Rechaza() =>
        Assert.Equal("El nombre es obligatorio", ObraValidator.Guardar("  ", "LP 123/25"));

    [Fact]
    public void Guardar_SinLicitacion_Rechaza() =>
        Assert.Equal("La licitación es obligatoria", ObraValidator.Guardar("Estación Sáenz", null));

    // ── Eliminar (mensaje de dependencias) ───────────────────────────────────────

    [Fact]
    public void Eliminar_SinDependencias_Pasa() =>
        Assert.Null(ObraValidator.Eliminar(0, 0, 0, 0, 0));

    [Fact]
    public void Eliminar_ConTodoElHistorial_EnumeraTodasLasPartesEnOrden() =>
        Assert.Equal(
            "La obra tiene 3 certificado(s), 2 redeterminación(es), 1 estructura(s) de costos, "
            + "tabla de ponderación, planificación asociados. "
            + "Eliminá primero ese historial si realmente corresponde borrarla.",
            ObraValidator.Eliminar(3, 2, 1, 1, 1));

    [Fact]
    public void Eliminar_SoloCertificados_MencionaSoloEso()
    {
        var error = ObraValidator.Eliminar(5, 0, 0, 0, 0);
        Assert.Contains("5 certificado(s)", error);
        Assert.DoesNotContain("redeterminación", error);
        Assert.DoesNotContain("estructura", error);
        Assert.DoesNotContain("tabla de ponderación", error);
        Assert.DoesNotContain("planificación", error);
    }

    [Fact]
    public void Eliminar_TablaYPlanificacion_VanSinConteo()
    {
        // La tabla y la planificación se nombran sin cantidad (mensaje histórico de la grilla).
        var error = ObraValidator.Eliminar(0, 0, 0, 2, 3);
        Assert.Equal(
            "La obra tiene tabla de ponderación, planificación asociados. "
            + "Eliminá primero ese historial si realmente corresponde borrarla.", error);
    }

    [Fact]
    public void Eliminar_SoloRedeterminaciones_UsaSuEtiqueta()
    {
        var error = ObraValidator.Eliminar(0, 1, 0, 0, 0);
        Assert.Contains("1 redeterminación(es)", error);
    }
}
