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

    [Theory]
    [InlineData(null, "obligatorio")]
    [InlineData("   ", "obligatorio")]
    [InlineData("REVISTA_AGOSTO", "INDEC_INFORMA_MM_AA")]
    public async Task Importar_SinPublicacionValida_RechazaYNoInserta(string? idPublicacion, string mensajeEsperado)
    {
        // Sin id, los valores quedan con IdPublicacion NULL: invisibles como publicación
        // en Calcular (filtra != null) y mezclados con las tasas BN. El alta manual
        // (AgregarValorAsync) sí admite publicación vacía — la regla es solo del import.
        var filas = new List<FilaImport> { new() { IndiceId = 1, Anio = 2043, Mes = 1, Valor = 100m } };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Servicio(Roles.Admin).ImportarAsync(filas, idPublicacion));
        // Fragmento distintivo de cada regla: «publicación» a secas también aparece en el
        // mensaje de falta de rol y el test pasaría por la razón equivocada.
        Assert.Contains(mensajeEsperado, ex.Message);

        await using var db = fx.CrearContexto();
        Assert.False(await db.ValoresIndice.AnyAsync(v => v.IndiceId == 1 && v.Anio == 2043));
    }

    [Fact]
    public async Task Importar_ConPublicacionValida_InsertaSoloOkYTrimeaElId()
    {
        // Solo entran las filas Ok del análisis; el id se guarda trimeado, igual que lo
        // valida IndiceValidator.PublicacionImportacion (si no, «Ya existe» no lo vería).
        var filas = new List<FilaImport>
        {
            new() { IndiceId = 1, Anio = 2044, Mes = 1, Valor = 100m, Estado = EstadoFilaImport.Ok },
            new() { IndiceId = 1, Anio = 2044, Mes = 2, Valor = 101m, Estado = EstadoFilaImport.YaExiste },
            new() { IndiceId = 1, Anio = 2044, Mes = 3, Valor = 102m, Estado = EstadoFilaImport.CodigoDesconocido }
        };

        var cantidad = await Servicio(Roles.Admin).ImportarAsync(filas, "  INDEC_INFORMA_01_44  ");
        Assert.Equal(1, cantidad);

        await using var db = fx.CrearContexto();
        var insertada = await db.ValoresIndice.SingleAsync(v => v.IndiceId == 1 && v.Anio == 2044);
        Assert.Equal(1, insertada.Mes);
        Assert.Equal(100m, insertada.Valor);
        Assert.Equal("INDEC_INFORMA_01_44", insertada.IdPublicacion);
    }

    [Fact]
    public async Task AnalizarImportacion_TrasAltaRapida_ResuelveElCodigo()
    {
        // Flujo del botón «Agregar al catálogo»: la fila sale «Código desconocido», se crea
        // el índice con el código tal como vino (acá en minúsculas: el match no distingue
        // mayúsculas) y el re-análisis del mismo pegado la deja «A importar».
        var codigo = $"ALTA_{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        var pegado = $"{codigo.ToLowerInvariant()}\t100,5";

        var filas = ImportadorIndec.ParsearLineas(pegado, 2045, 1);
        await Servicio(Roles.Admin).AnalizarImportacionAsync(filas, "INDEC_INFORMA_01_45");
        Assert.Equal(EstadoFilaImport.CodigoDesconocido, Assert.Single(filas).Estado);

        await Servicio(Roles.Admin).CrearAsync(new IndiceVM { CodigoIndice = filas[0].Codigo, FamiliaRecurso = "Alta rápida" });

        filas = ImportadorIndec.ParsearLineas(pegado, 2045, 1);
        await Servicio(Roles.Admin).AnalizarImportacionAsync(filas, "INDEC_INFORMA_01_45");
        Assert.Equal(EstadoFilaImport.Ok, Assert.Single(filas).Estado);
    }

    [Fact]
    public async Task AgregarValor_SinPublicacion_Guarda()
    {
        // Decisión 2026-10-07: la publicación obligatoria rige solo en el import. El alta
        // manual admite publicación vacía (tasas Banco Nación), que se guarda como NULL.
        var nuevo = new NuevoValorVM { Anio = 2041, Mes = 3, Valor = 1m, IdPublicacion = "   " };

        Assert.True(await Servicio(Roles.Admin).AgregarValorAsync(1, nuevo));

        await using var db = fx.CrearContexto();
        var fila = await db.ValoresIndice.SingleAsync(v => v.IndiceId == 1 && v.Anio == 2041 && v.Mes == 3);
        Assert.Null(fila.IdPublicacion);
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
