using SIGO.Services.Validaciones;

namespace SIGO.Tests;

/// <summary>
/// Reglas puras de la tabla de ponderación (PonderacionValidator): alta/edición de
/// insumo (nombre, peso en (0,100], índice obligatorio) y la protección de FK Restrict
/// al eliminar un insumo referenciado por redeterminaciones guardadas.
/// </summary>
public class PonderacionValidatorTests
{
    // ── GuardarItem ──────────────────────────────────────────────────────────────

    [Fact]
    public void GuardarItem_Completo_Pasa() =>
        Assert.Null(PonderacionValidator.GuardarItem("Mano de Obra", 10m, 1));

    [Fact]
    public void GuardarItem_InsumoVacioONulo_Rechaza()
    {
        Assert.Equal("El insumo es obligatorio.", PonderacionValidator.GuardarItem(null, 10m, 1));
        Assert.Equal("El insumo es obligatorio.", PonderacionValidator.GuardarItem("   ", 10m, 1));
    }

    [Fact]
    public void GuardarItem_PesoNulo_Rechaza() =>
        Assert.Equal("El peso debe ser mayor que 0 y como máximo 100.",
            PonderacionValidator.GuardarItem("Mano de Obra", null, 1));

    [Fact]
    public void GuardarItem_PesoCeroONegativo_Rechaza()
    {
        Assert.NotNull(PonderacionValidator.GuardarItem("Mano de Obra", 0m, 1));
        Assert.NotNull(PonderacionValidator.GuardarItem("Mano de Obra", -5m, 1));
    }

    [Fact]
    public void GuardarItem_PesoMayorA100_Rechaza() =>
        Assert.NotNull(PonderacionValidator.GuardarItem("Mano de Obra", 100.01m, 1));

    [Fact]
    public void GuardarItem_PesoEnLosBordes_Pasa()
    {
        // El rango es (0, 100]: el mínimo cargable de la UI y el 100 exacto son válidos.
        Assert.Null(PonderacionValidator.GuardarItem("Mano de Obra", 0.0001m, 1));
        Assert.Null(PonderacionValidator.GuardarItem("Mano de Obra", 100m, 1));
    }

    [Fact]
    public void GuardarItem_SinIndice_Rechaza() =>
        Assert.Equal("Seleccioná el índice INDEC.",
            PonderacionValidator.GuardarItem("Mano de Obra", 10m, null));

    // ── EliminarItem ─────────────────────────────────────────────────────────────

    [Fact]
    public void EliminarItem_SinUsos_Pasa() =>
        Assert.Null(PonderacionValidator.EliminarItem("Mano de Obra", usos: 0));

    [Fact]
    public void EliminarItem_Referenciado_RechazaConElDetalle() =>
        Assert.Equal("El insumo «Mano de Obra» está referenciado por 3 ítem(s) de redeterminaciones guardadas y no puede eliminarse.",
            PonderacionValidator.EliminarItem("Mano de Obra", usos: 3));
}
