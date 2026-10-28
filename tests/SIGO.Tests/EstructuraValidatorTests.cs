using SIGO.Models.ViewModels;
using SIGO.Services.Validaciones;

namespace SIGO.Tests;

/// <summary>
/// Reglas de normalización de ítems de estructura, ahora puras en EstructuraValidator:
/// estos tests fijan que los agrupadores no llevan código/unidad/cantidad/PU/monto,
/// que Monto = Cantidad × PU solo si AMBOS existen (redondeado a 4), y la asimetría
/// alta/edición del padre de un agrupador (el alta lo fuerza a raíz; la edición lo
/// conserva porque los sub-rubros anidados existen).
/// </summary>
public class EstructuraValidatorTests
{
    // ── Monto = Cantidad × PU solo si AMBOS existen ──────────────────────────────

    [Fact]
    public void Monto_ConCantidadYPU_MultiplicaYRedondeaA4() =>
        // 2.4691 × 10.0001 = 24.69124691 → 24.6912 con 4 decimales.
        Assert.Equal(24.6912m, EstructuraValidator.CalcularMonto(false, 2.4691m, 10.0001m));

    [Fact]
    public void Monto_SoloCantidad_EsNull() =>
        Assert.Null(EstructuraValidator.CalcularMonto(false, 5m, null));

    [Fact]
    public void Monto_SoloPU_EsNull()
    {
        // Ítem global "1 gl" cargado sin cantidad: no hay monto que derivar.
        Assert.Null(EstructuraValidator.CalcularMonto(false, null, 100m));
    }

    [Fact]
    public void Monto_Agrupador_EsNullAunqueTengaValores() =>
        Assert.Null(EstructuraValidator.CalcularMonto(true, 5m, 100m));

    // ── Normalización de agrupadores ─────────────────────────────────────────────

    [Fact]
    public void Normalizar_Agrupador_SinCodigoUnidadCantidadPUNiMonto()
    {
        var n = EstructuraValidator.Normalizar(
            esAgrupador: true, agrupadorPadreId: 7, codigo: "R.1", descripcion: " Rubro General ",
            unidad: "gl", cantidad: 1m, puBasico: 100m);

        Assert.Null(n.Codigo);
        Assert.Null(n.Unidad);
        Assert.Null(n.Cantidad);
        Assert.Null(n.PUBasico);
        Assert.Null(n.Monto);
        Assert.Equal("Rubro General", n.Descripcion);
    }

    [Fact]
    public void Normalizar_AltaDeAgrupador_FuerzaPadreNull() =>
        // En el alta un agrupador nace en la raíz (el form no ofrece padre).
        Assert.Null(EstructuraValidator.Normalizar(
            esAgrupador: true, agrupadorPadreId: 7, codigo: null, descripcion: "Rubro",
            unidad: null, cantidad: null, puBasico: null).AgrupadorPadreId);

    [Fact]
    public void Normalizar_EdicionDeAgrupador_ConservaElPadre() =>
        // Sub-rubro anidado: editarle la descripción no debe reparentarlo a raíz.
        Assert.Equal(7, EstructuraValidator.Normalizar(
            esAgrupador: true, agrupadorPadreId: 7, codigo: null, descripcion: "Sub-rubro",
            unidad: null, cantidad: null, puBasico: null, conservarPadreDeAgrupador: true).AgrupadorPadreId);

    // ── Normalización de ítems hoja ──────────────────────────────────────────────

    [Fact]
    public void Normalizar_ItemHoja_ConservaValoresYRecortaTextos()
    {
        var n = EstructuraValidator.Normalizar(
            esAgrupador: false, agrupadorPadreId: 3, codigo: " P.GEN.1.1 ", descripcion: " Excavación ",
            unidad: " m3 ", cantidad: 2m, puBasico: 10m);

        Assert.Equal(3, n.AgrupadorPadreId);
        Assert.Equal("P.GEN.1.1", n.Codigo);
        Assert.Equal("Excavación", n.Descripcion);
        Assert.Equal("m3", n.Unidad);
        Assert.Equal(2m, n.Cantidad);
        Assert.Equal(10m, n.PUBasico);
        Assert.Equal(20m, n.Monto);
    }

    [Fact]
    public void Normalizar_ItemHoja_SinPU_SinMonto()
    {
        var n = EstructuraValidator.Normalizar(
            esAgrupador: false, agrupadorPadreId: null, codigo: null, descripcion: "Ítem global",
            unidad: "gl", cantidad: 1m, puBasico: null);

        Assert.Equal(1m, n.Cantidad);
        Assert.Null(n.Monto);
    }

    [Fact]
    public void Normalizar_DescripcionNull_QuedaVacia() =>
        Assert.Equal(string.Empty, EstructuraValidator.Normalizar(
            esAgrupador: false, agrupadorPadreId: null, codigo: null, descripcion: null,
            unidad: null, cantidad: null, puBasico: null).Descripcion);

    [Fact]
    public void Normalizar_SobreElVm_NoLoMuta()
    {
        var vm = new ItemEstructuraVM
        {
            EsAgrupador = true, AgrupadorPadreId = 4, Codigo = "X",
            Descripcion = " Rubro ", Unidad = "gl", Cantidad = 1m, PUBasico = 2m
        };

        var n = EstructuraValidator.Normalizar(vm);

        Assert.Null(n.Codigo);
        Assert.Null(n.AgrupadorPadreId);
        // El VM original queda intacto (la normalización devuelve un record nuevo).
        Assert.Equal("X", vm.Codigo);
        Assert.Equal(4, vm.AgrupadorPadreId);
    }

    // ── Obligatorios (mismos mensajes que los RequiredValidator de las páginas) ──

    [Fact]
    public void NombreEstructura_Vacio_Rechaza()
    {
        Assert.Equal("El nombre es obligatorio", EstructuraValidator.NombreEstructura(null));
        Assert.Equal("El nombre es obligatorio", EstructuraValidator.NombreEstructura("   "));
        Assert.Null(EstructuraValidator.NombreEstructura("Estructura Original"));
    }

    [Fact]
    public void DescripcionItem_Vacia_Rechaza()
    {
        Assert.Equal("La descripción es obligatoria", EstructuraValidator.DescripcionItem(null));
        Assert.Equal("La descripción es obligatoria", EstructuraValidator.DescripcionItem("   "));
        Assert.Null(EstructuraValidator.DescripcionItem("Excavación"));
    }

    // ── Rango de decimal(18,4) ───────────────────────────────────────────────────

    [Fact]
    public void Rango_DentroDelMaximo_YSinValores_Pasa()
    {
        Assert.Null(EstructuraValidator.Rango(null, null));
        Assert.Null(EstructuraValidator.Rango(-5m, 1000m));   // los negativos no son un problema de rango
        Assert.Null(EstructuraValidator.Rango(1m, EstructuraValidator.MaximoAlmacenable));
    }

    [Theory]
    [InlineData(1e15, 1.0, "cantidad")]
    [InlineData(1.0, -1e15, "PU")]
    [InlineData(1e8, 1e8, "monto")]          // cada uno entra; el producto (1e16) no
    public void Rango_FueraDelMaximo_RechazaDiciendoQue(double cantidad, double pu, string que) =>
        Assert.Contains(que, EstructuraValidator.Rango((decimal)cantidad, (decimal)pu));

    [Fact]
    public void NoNegativos_RechazaCantidadOPUNegativos()
    {
        Assert.Null(EstructuraValidator.NoNegativos(null, null));
        Assert.Null(EstructuraValidator.NoNegativos(0m, 0m));
        Assert.Equal("La cantidad no puede ser negativa.", EstructuraValidator.NoNegativos(-1m, 10m));
        Assert.Equal("El PU no puede ser negativo.", EstructuraValidator.NoNegativos(1m, -0.01m));
    }

    [Fact]
    public void CantidadYPU_PorCampo_SonLasReglasDeNoNegativos()
    {
        // Los forms de ítems evalúan cada campo con estas reglas: vacío pasa (agrupadores).
        Assert.Null(EstructuraValidator.Cantidad(null));
        Assert.Null(EstructuraValidator.Cantidad(0m));
        Assert.Equal(EstructuraValidator.CantidadNegativa, EstructuraValidator.Cantidad(-1m));
        Assert.Null(EstructuraValidator.PUBasico(null));
        Assert.Equal(EstructuraValidator.PUNegativo, EstructuraValidator.PUBasico(-500m));
        Assert.Equal(EstructuraValidator.PUNegativo, EstructuraValidator.NoNegativos(1m, -500m));
    }

    [Fact]
    public void Valores_PrimeroElSignoDespuesElRango()
    {
        Assert.Null(EstructuraValidator.Valores(2m, 10m));
        Assert.Contains("negativa", EstructuraValidator.Valores(-1e15m, 1m));   // ambas fallan: avisa el signo
        Assert.Contains("máximo", EstructuraValidator.Valores(1e15m, 1m));
    }

    [Fact]
    public void Rango_FactoresExtremos_NoDesborda() =>
        // 7,9e28 es el máximo de decimal: el producto se evalúa solo con factores en rango.
        Assert.NotNull(EstructuraValidator.Rango(7e28m, 7e28m));

    // ── Padre de un ítem ─────────────────────────────────────────────────────────

    // Rubro 1 › (Sub-rubro 2 › Ítem 3); Ítem 4 en la raíz.
    private static readonly (int Id, bool EsAgrupador, int? PadreId)[] ItemsDeEstructura =
        [(1, true, null), (2, true, 1), (3, false, 2), (4, false, null)];

    [Fact]
    public void Padre_AgrupadorDeLaEstructura_OSinPadre_Pasa()
    {
        Assert.Null(EstructuraValidator.Padre(null, 4, ItemsDeEstructura));
        Assert.Null(EstructuraValidator.Padre(2, 4, ItemsDeEstructura));
        Assert.Null(EstructuraValidator.Padre(1, 0, ItemsDeEstructura));    // alta
    }

    [Fact]
    public void Padre_DeOtraEstructuraNoAgrupadorOElMismoItem_Rechaza()
    {
        Assert.Contains("no pertenece", EstructuraValidator.Padre(99, 4, ItemsDeEstructura));
        Assert.Contains("no es un agrupador", EstructuraValidator.Padre(3, 4, ItemsDeEstructura));
        Assert.Contains("propio", EstructuraValidator.Padre(2, 2, ItemsDeEstructura));
    }

    [Fact]
    public void Padre_QueCuelgaDelItem_FormariaUnCiclo() =>
        // El rubro 1 no puede pasar a colgar del sub-rubro 2, que cuelga de él.
        Assert.Contains("ciclo", EstructuraValidator.Padre(2, 1, ItemsDeEstructura));

    [Fact]
    public void Normalizar_RedondeaCantidadYPUAntesDelMonto()
    {
        // Como la importación: el monto sale de lo que se guarda (4 decimales). Antes se guardaba
        // la cantidad redondeada y el monto de la sin redondear ($3,17 de diferencia al 100 %).
        var n = EstructuraValidator.Normalizar(false, null, "1.1", "Ítem", "m3", 6.88905m, 63421.20004m);

        Assert.Equal(Math.Round(6.88905m, 4), n.Cantidad);
        Assert.Equal(63421.2m, n.PUBasico);
        Assert.Equal(Math.Round(n.Cantidad!.Value * n.PUBasico!.Value, 4), n.Monto);
        Assert.NotEqual(Math.Round(6.88905m * 63421.20004m, 4), n.Monto);
    }
}
