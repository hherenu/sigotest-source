using Microsoft.EntityFrameworkCore;
using SIGO.Models;
using SIGO.Models.Enums;
using SIGO.Services;

namespace SIGO.Tests;

/// <summary>
/// Cálculo Ki/K0 y guardado de disparos contra una LocalDB real (mismo proveedor,
/// mismos índices únicos y transacciones Serializable que producción).
/// </summary>
public class RedeterminacionServiceTests(LocalDbFixture fx) : IClassFixture<LocalDbFixture>
{
    private RedeterminacionService Servicio(params string[] roles) =>
        new(new TestDbFactory(fx.Options), new FakeCurrentUser(roles));

    /// <summary>Obra + tabla con dos insumos (60/40) sobre los índices 1 y 2 del catálogo seedeado.</summary>
    private async Task<(int ObraId, int TablaId)> CrearObraConTablaAsync(decimal peso1 = 60m, decimal peso2 = 40m)
    {
        await using var db = fx.CrearContexto();
        var obra = new Obra { Nombre = $"Obra test {Guid.NewGuid():N}", NumeroLicitacion = "LP TEST" };
        db.Obras.Add(obra);
        await db.SaveChangesAsync();

        var tabla = new TablaPonderacion { ObraId = obra.Id, Nombre = "Tabla test" };
        db.TablasPonderacion.Add(tabla);
        await db.SaveChangesAsync();

        db.ItemsPonderacion.AddRange(
            new ItemPonderacion { TablaPonderacionId = tabla.Id, Numero = 1, Insumo = "Insumo A", PesoPorcentaje = peso1, IndiceId = 1 },
            new ItemPonderacion { TablaPonderacionId = tabla.Id, Numero = 2, Insumo = "Insumo B", PesoPorcentaje = peso2, IndiceId = 2 });
        await db.SaveChangesAsync();
        return (obra.Id, tabla.Id);
    }

    private async Task CargarValoresAsync(params (int IndiceId, int Anio, int Mes, decimal Valor, string? Pub)[] valores)
    {
        await using var db = fx.CrearContexto();
        db.ValoresIndice.AddRange(valores.Select(v => new ValorIndice
        {
            IndiceId = v.IndiceId, Anio = v.Anio, Mes = v.Mes, Valor = v.Valor, IdPublicacion = v.Pub
        }));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Calcular_KiK0VariacionYTotal()
    {
        var (obraId, tablaId) = await CrearObraConTablaAsync();
        await CargarValoresAsync(
            (1, 2030, 1, 100m, "PUB_T1"), (1, 2030, 6, 110m, "PUB_T1"),
            (2, 2030, 1, 200m, "PUB_T1"), (2, 2030, 6, 250m, "PUB_T1"));

        var vm = await Servicio().CalcularAsync(obraId, tablaId, 2030, 1, "PUB_T1", 2030, 6, "PUB_T1");

        // Ki/K0 a 6 decimales; variación ponderada = KiK0 × peso/100.
        Assert.Equal(1.100000m, vm.Items[0].KiK0);
        Assert.Equal(0.660000m, vm.Items[0].VariacionPonderada);
        Assert.Equal(1.250000m, vm.Items[1].KiK0);
        Assert.Equal(0.500000m, vm.Items[1].VariacionPonderada);
        Assert.Equal(1.160000m, vm.TotalKiK0);
        Assert.Equal(16.0000m, vm.PorcentajeAumento);
    }

    [Fact]
    public async Task Calcular_PesosQueNoSuman100_Rechaza()
    {
        var (obraId, tablaId) = await CrearObraConTablaAsync(peso1: 60m, peso2: 30m);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Servicio().CalcularAsync(obraId, tablaId, 2031, 1, null, 2031, 6, null));
        Assert.Contains("100%", ex.Message);
    }

    [Fact]
    public async Task Calcular_ConValorFaltante_DejaTotalEnNull()
    {
        var (obraId, tablaId) = await CrearObraConTablaAsync();
        // El índice 2 no tiene valor en el mes salto: su KiK0 y el total quedan null.
        await CargarValoresAsync(
            (1, 2032, 1, 100m, "PUB_T3"), (1, 2032, 6, 120m, "PUB_T3"),
            (2, 2032, 1, 200m, "PUB_T3"));

        var vm = await Servicio().CalcularAsync(obraId, tablaId, 2032, 1, "PUB_T3", 2032, 6, "PUB_T3");

        Assert.NotNull(vm.Items[0].KiK0);
        Assert.Null(vm.Items[1].KiK0);
        Assert.Null(vm.TotalKiK0);
        Assert.Null(vm.PorcentajeAumento);
    }

    [Fact]
    public async Task Calcular_ConValorSaltoCero_DejaKiK0EnNull()
    {
        var (obraId, tablaId) = await CrearObraConTablaAsync();
        // Un 0 en el salto (dato inválido que pudo entrar antes de la validación del
        // alta) no puede producir coeficiente: KiK0 = 0 entraría a la cadena de
        // VariacionAcumulada. Se trata igual que un valor faltante.
        await CargarValoresAsync(
            (1, 2037, 1, 100m, "PUB_T8"), (1, 2037, 6, 120m, "PUB_T8"),
            (2, 2037, 1, 200m, "PUB_T8"), (2, 2037, 6, 0m, "PUB_T8"));

        var vm = await Servicio().CalcularAsync(obraId, tablaId, 2037, 1, "PUB_T8", 2037, 6, "PUB_T8");

        Assert.NotNull(vm.Items[0].KiK0);
        Assert.Null(vm.Items[1].KiK0);
        Assert.Null(vm.TotalKiK0);
    }

    [Fact]
    public async Task Calcular_PrefiereLaPublicacionPedida()
    {
        var (obraId, tablaId) = await CrearObraConTablaAsync(peso1: 100m, peso2: 0m);
        // El mismo mes existe bajo dos publicaciones y sin publicación: la selección
        // debe ser determinística (pedida > sin publicación > más reciente).
        await CargarValoresAsync(
            (1, 2033, 1, 100m, "PUB_VIEJA"), (1, 2033, 1, 999m, "PUB_NUEVA"), (1, 2033, 1, 500m, null),
            (2, 2033, 1, 1m, "PUB_VIEJA"),
            (1, 2033, 6, 200m, "PUB_VIEJA"), (2, 2033, 6, 1m, "PUB_VIEJA"));

        var vm = await Servicio().CalcularAsync(obraId, tablaId, 2033, 1, "PUB_VIEJA", 2033, 6, "PUB_VIEJA");

        // Base 100 (la de PUB_VIEJA), no 999 ni 500: KiK0 = 200/100.
        Assert.Equal(2.000000m, vm.Items[0].KiK0);
    }

    [Fact]
    public async Task GuardarDisparo_AsignaNroYEncadenaVariacionAcumulada()
    {
        var (obraId, tablaId) = await CrearObraConTablaAsync();
        await CargarValoresAsync(
            (1, 2034, 1, 100m, "PUB_T5"), (1, 2034, 6, 110m, "PUB_T5"), (1, 2034, 12, 121m, "PUB_T5"),
            (2, 2034, 1, 100m, "PUB_T5"), (2, 2034, 6, 110m, "PUB_T5"), (2, 2034, 12, 121m, "PUB_T5"));
        var servicio = Servicio(Roles.Admin);

        var vm1 = await servicio.CalcularAsync(obraId, tablaId, 2034, 1, "PUB_T5", 2034, 6, "PUB_T5");
        await servicio.GuardarDisparoAsync(obraId, tablaId, vm1);
        var vm2 = await servicio.CalcularAsync(obraId, tablaId, 2034, 6, "PUB_T5", 2034, 12, "PUB_T5");
        await servicio.GuardarDisparoAsync(obraId, tablaId, vm2);

        await using var db = fx.CrearContexto();
        var disparos = await db.RedeterminacionesGuardadas
            .Where(r => r.ObraId == obraId).OrderBy(r => r.NroDisparo).Include(r => r.Items).ToListAsync();

        Assert.Equal([1, 2], disparos.Select(d => d.NroDisparo));
        // Ambos saltos son +10%: acumulada = 1.1 y 1.1 × 1.1.
        Assert.Equal(1.100000m, disparos[0].VariacionAcumulada);
        Assert.Equal(1.210000m, disparos[1].VariacionAcumulada);
        Assert.Equal(2, disparos[0].Items.Count); // snapshot por insumo
        Assert.Equal(EstadoRedeterminacion.Calculada, disparos[0].Estado);
    }

    [Fact]
    public async Task GuardarDisparo_SinRol_Rechaza()
    {
        var (obraId, tablaId) = await CrearObraConTablaAsync();
        await CargarValoresAsync(
            (1, 2035, 1, 100m, "PUB_T6"), (1, 2035, 6, 110m, "PUB_T6"),
            (2, 2035, 1, 100m, "PUB_T6"), (2, 2035, 6, 110m, "PUB_T6"));

        var vm = await Servicio().CalcularAsync(obraId, tablaId, 2035, 1, "PUB_T6", 2035, 6, "PUB_T6");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Servicio(Roles.Director).GuardarDisparoAsync(obraId, tablaId, vm));
        Assert.Contains("permiso", ex.Message);
    }

    [Fact]
    public async Task RecalcularDisparo_PresentadoOAprobado_Rechaza()
    {
        var (obraId, tablaId) = await CrearObraConTablaAsync();
        await CargarValoresAsync(
            (1, 2036, 1, 100m, "PUB_T7"), (1, 2036, 6, 110m, "PUB_T7"),
            (2, 2036, 1, 100m, "PUB_T7"), (2, 2036, 6, 110m, "PUB_T7"));
        var servicio = Servicio(Roles.Admin);

        var vm = await servicio.CalcularAsync(obraId, tablaId, 2036, 1, "PUB_T7", 2036, 6, "PUB_T7");
        await servicio.GuardarDisparoAsync(obraId, tablaId, vm);

        int disparoId;
        await using (var db = fx.CrearContexto())
        {
            var disparo = await db.RedeterminacionesGuardadas.SingleAsync(r => r.ObraId == obraId);
            disparo.Estado = EstadoRedeterminacion.Presentada;
            await db.SaveChangesAsync();
            disparoId = disparo.Id;
        }

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            servicio.RecalcularDisparoAsync(disparoId, obraId, tablaId, vm));
        Assert.Contains("no admite recálculo", ex.Message);
    }

    [Fact]
    public async Task UltimoSalto_NullSinDisparosYDevuelveElDelUltimo()
    {
        var (obraId, tablaId) = await CrearObraConTablaAsync();
        Assert.Null(await Servicio().UltimoSaltoAsync(obraId));

        await CargarValoresAsync(
            (1, 2042, 1, 100m, "PUB_T11"), (1, 2042, 6, 110m, "PUB_T11"),
            (2, 2042, 1, 100m, "PUB_T11"), (2, 2042, 6, 110m, "PUB_T11"));
        var servicio = Servicio(Roles.Admin);
        var vm = await servicio.CalcularAsync(obraId, tablaId, 2042, 1, "PUB_T11", 2042, 6, "PUB_T11");
        await servicio.GuardarDisparoAsync(obraId, tablaId, vm);

        // Alimenta el aviso de continuidad de Calcular (base = salto del último).
        Assert.Equal((2042, 6), await Servicio().UltimoSaltoAsync(obraId));
    }

    [Fact]
    public async Task RecalcularDisparo_DeOtraObra_Rechaza()
    {
        var (obraId, tablaId) = await CrearObraConTablaAsync();
        var (otraObraId, otraTablaId) = await CrearObraConTablaAsync();
        await CargarValoresAsync(
            (1, 2038, 1, 100m, "PUB_T9"), (1, 2038, 6, 110m, "PUB_T9"),
            (2, 2038, 1, 100m, "PUB_T9"), (2, 2038, 6, 110m, "PUB_T9"));
        var servicio = Servicio(Roles.Admin);

        var vm = await servicio.CalcularAsync(obraId, tablaId, 2038, 1, "PUB_T9", 2038, 6, "PUB_T9");
        await servicio.GuardarDisparoAsync(obraId, tablaId, vm);

        int disparoId;
        await using (var db = fx.CrearContexto())
            disparoId = (await db.RedeterminacionesGuardadas.SingleAsync(r => r.ObraId == obraId)).Id;

        // El recálculo reemplaza el cálculo, nunca muda el disparo de obra: eso dejaría
        // la cadena de VariacionAcumulada de la obra origen con un coeficiente fantasma.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            servicio.RecalcularDisparoAsync(disparoId, otraObraId, otraTablaId, vm));
        Assert.Contains("otra obra", ex.Message);

        await using var db2 = fx.CrearContexto();
        Assert.Equal(obraId, (await db2.RedeterminacionesGuardadas.SingleAsync(r => r.Id == disparoId)).ObraId);
    }

    [Fact]
    public async Task GuardarDisparo_ConTablaDeOtraObra_Rechaza()
    {
        var (obraId, tablaId) = await CrearObraConTablaAsync();
        var (_, otraTablaId) = await CrearObraConTablaAsync();
        await CargarValoresAsync(
            (1, 2039, 1, 100m, "PUB_T10"), (1, 2039, 6, 110m, "PUB_T10"),
            (2, 2039, 1, 100m, "PUB_T10"), (2, 2039, 6, 110m, "PUB_T10"));
        var servicio = Servicio(Roles.Admin);

        // El cálculo es legítimo (tabla propia); lo inválido es persistir el disparo
        // apuntando a la tabla de otra obra (referencia cruzada).
        var vm = await servicio.CalcularAsync(obraId, tablaId, 2039, 1, "PUB_T10", 2039, 6, "PUB_T10");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            servicio.GuardarDisparoAsync(obraId, otraTablaId, vm));
        Assert.Contains("no pertenece", ex.Message);
    }

    [Fact]
    public async Task GetRevistasDisponibles_OrdenaLasPublicacionesPorPeriodoNoPorTexto()
    {
        // El sufijo del id es "MM_AA": ordenarlo como texto (o en SQL) pone
        // INDEC_INFORMA_12_26 DESPUÉS de INDEC_INFORMA_01_27, y Calcular preselecciona
        // la última de la lista → arrancaba con la revista del año viejo.
        await CargarValoresAsync(
            (1, 2026, 12, 100m, "INDEC_INFORMA_12_26"),
            (1, 2027, 1, 110m, "INDEC_INFORMA_01_27"),
            (1, 2026, 4, 90m, "INDEC_INFORMA_04_26"));

        var revistas = await Servicio(Roles.Admin).GetRevistasDisponiblesAsync();
        var publicaciones = revistas.Select(r => r.IdPublicacion).Distinct().ToList();

        Assert.Equal("INDEC_INFORMA_01_27", publicaciones.Last());
        Assert.True(publicaciones.IndexOf("INDEC_INFORMA_04_26") < publicaciones.IndexOf("INDEC_INFORMA_12_26"));
    }
}
