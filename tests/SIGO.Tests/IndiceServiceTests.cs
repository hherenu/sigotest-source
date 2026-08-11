using Microsoft.EntityFrameworkCore;
using SIGO.Models.ViewModels;
using SIGO.Services;

namespace SIGO.Tests;

/// <summary>
/// Alta manual de valores mensuales contra LocalDB real: la regla "valor &gt; 0" del
/// import (IndiceValidator.ValorMensual) también rige acá — un 0 guardado envenena
/// el Ki/K0 de las redeterminaciones.
/// </summary>
public class IndiceServiceTests(LocalDbFixture fx) : IClassFixture<LocalDbFixture>
{
    private IndiceService Servicio(params string[] roles) =>
        new(new TestDbFactory(fx.Options), new FakeCurrentUser(roles));

    [Theory]
    [InlineData(0d)]
    [InlineData(-5d)]
    [InlineData(null)]
    public async Task AgregarValor_NoPositivo_Rechaza(double? valor)
    {
        var nuevo = new NuevoValorVM { Anio = 2040, Mes = 1, Valor = (decimal?)valor };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Servicio(Roles.Admin).AgregarValorAsync(1, nuevo));
        Assert.Contains("mayor que cero", ex.Message);

        await using var db = fx.CrearContexto();
        Assert.False(await db.ValoresIndice.AnyAsync(v => v.IndiceId == 1 && v.Anio == 2040));
    }

    [Theory]
    [InlineData("   ", "Mano de obra")]
    [InlineData("MANO_OBRA_T", "   ")]
    public async Task Crear_ConObligatorioSoloEspacios_Rechaza(string codigo, string familia)
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Servicio(Roles.Admin).CrearAsync(new IndiceVM { CodigoIndice = codigo, FamiliaRecurso = familia }));
        Assert.Contains("obligatori", ex.Message);
    }

    [Fact]
    public async Task Crear_TrimeaYRechazaElCodigoDuplicado()
    {
        var codigo = $"IDX_{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        // El import matchea por código exacto: un espacio al inicio dejaba el índice
        // inalcanzable ("Código desconocido" en cada pegado).
        var id = await Servicio(Roles.Admin).CrearAsync(new IndiceVM
        {
            CodigoIndice = $"  {codigo} ",
            FamiliaRecurso = "  Familia test  ",
            IndiceNormalizado = "   "
        });

        await using var db = fx.CrearContexto();
        var indice = await db.IndicesINDEC.SingleAsync(i => i.Id == id);
        Assert.Equal(codigo, indice.CodigoIndice);
        Assert.Equal("Familia test", indice.FamiliaRecurso);
        Assert.Null(indice.IndiceNormalizado);   // opcional en blanco → null, no espacios

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Servicio(Roles.Admin).CrearAsync(new IndiceVM { CodigoIndice = codigo, FamiliaRecurso = "Otra familia" }));
        Assert.Contains("Ya existe", ex.Message);
    }

    [Fact]
    public async Task AgregarValor_Positivo_Guarda()
    {
        var nuevo = new NuevoValorVM { Anio = 2041, Mes = 2, Valor = 123.45m, IdPublicacion = "PUB_IDX" };

        Assert.True(await Servicio(Roles.Admin).AgregarValorAsync(1, nuevo));

        await using var db = fx.CrearContexto();
        var fila = await db.ValoresIndice.SingleAsync(v => v.IndiceId == 1 && v.Anio == 2041 && v.Mes == 2);
        Assert.Equal(123.45m, fila.Valor);
    }
}
