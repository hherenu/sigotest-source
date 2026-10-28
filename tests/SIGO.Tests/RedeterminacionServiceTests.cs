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

    // ── Estado del trámite: expediente, baja y numeración ─────────────────────────

    /// <summary>
    /// Obra con <paramref name="cantidad"/> disparos guardados, tramos encadenados de +10%
    /// cada uno (mes k → k+1); hay valores hasta el mes cantidad+1.
    /// </summary>
    private async Task<(int ObraId, int TablaId, RedeterminacionService Servicio)> CrearDisparosAsync(int anio, string pub, int cantidad)
    {
        var (obraId, tablaId) = await CrearObraConTablaAsync();
        var valores = new List<(int, int, int, decimal, string?)>();
        var valor = 100m;
        for (var mes = 1; mes <= cantidad + 1; mes++, valor *= 1.1m)
            valores.AddRange([(1, anio, mes, valor, pub), (2, anio, mes, valor, pub)]);
        await CargarValoresAsync([.. valores]);

        var servicio = Servicio(Roles.Admin);
        for (var k = 1; k <= cantidad; k++)
            await servicio.GuardarDisparoAsync(obraId, tablaId, await servicio.CalcularAsync(obraId, tablaId, anio, k, pub, anio, k + 1, pub));
        return (obraId, tablaId, servicio);
    }

    private async Task<List<RedeterminacionGuardada>> DisparosAsync(int obraId)
    {
        await using var db = fx.CrearContexto();
        return await db.RedeterminacionesGuardadas.AsNoTracking()
            .Where(r => r.ObraId == obraId).OrderBy(r => r.NroDisparo).ThenBy(r => r.Id).ToListAsync();
    }

    /// <summary>Abre los datos administrativos del disparo, los modifica y los guarda con su token.</summary>
    private static async Task GuardarDatosAdminAsync(RedeterminacionService servicio, int disparoId, Action<SIGO.Models.ViewModels.RedeterminacionAdminVM> editar)
    {
        var admin = (await servicio.BuildAdminVmAsync(disparoId))!;
        editar(admin);
        await servicio.GuardarDatosAdminAsync(disparoId, admin, admin.RowVersion);
    }

    [Fact]
    public async Task ConExpedienteVR_QuedaPresentadaYNoSeRecalculaNiElimina_HastaQueSeBorraEditando()
    {
        var (obraId, tablaId, servicio) = await CrearDisparosAsync(2050, "PUB_A3_1", 1);
        var id = (await DisparosAsync(obraId)).Single().Id;

        await GuardarDatosAdminAsync(servicio, id, a => a.NroExpedienteVR = "EX-2026-00000001-GCABA-SBASE");

        var presentado = (await DisparosAsync(obraId)).Single();
        Assert.Equal(EstadoRedeterminacion.Presentada, presentado.Estado);
        var vm = await servicio.CalcularAsync(obraId, tablaId, 2050, 1, "PUB_A3_1", 2050, 2, "PUB_A3_1");
        Assert.Contains("no admite recálculo", (await Assert.ThrowsAsync<InvalidOperationException>(() =>
            servicio.RecalcularDisparoAsync(id, obraId, tablaId, vm))).Message);
        Assert.Contains("no puede eliminarse", (await Assert.ThrowsAsync<InvalidOperationException>(() =>
            servicio.EliminarDisparoAsync(id, presentado.RowVersion))).Message);

        // Una carga equivocada se corrige editando: sin expediente vuelve a Calculada.
        await GuardarDatosAdminAsync(servicio, id, a => a.NroExpedienteVR = null);
        Assert.Equal(EstadoRedeterminacion.Calculada, (await DisparosAsync(obraId)).Single().Estado);
        await servicio.RecalcularDisparoAsync(id, obraId, tablaId, vm);
    }

    [Fact]
    public async Task AnularElUltimo_ElSiguienteUsaSuNumeroYNoEncadenaElAnulado()
    {
        var (obraId, tablaId, servicio) = await CrearDisparosAsync(2051, "PUB_A3_2", 3);
        var tercero = (await DisparosAsync(obraId)).Single(d => d.NroDisparo == 3);

        await GuardarDatosAdminAsync(servicio, tercero.Id, a =>
        {
            a.NroExpedienteVR = "EX-2026-00000002-GCABA-SBASE";
            a.Baja = EstadoRedeterminacion.Anulada;
        });

        // El último vigente vuelve a ser el 2: su salto es la base del próximo.
        Assert.Equal((2051, 3), await servicio.UltimoSaltoAsync(obraId));
        await servicio.GuardarDisparoAsync(obraId, tablaId,
            await servicio.CalcularAsync(obraId, tablaId, 2051, 3, "PUB_A3_2", 2051, 4, "PUB_A3_2"));

        var disparos = await DisparosAsync(obraId);
        Assert.Equal(
            new[] { (1, EstadoRedeterminacion.Calculada), (2, EstadoRedeterminacion.Calculada),
                    (3, EstadoRedeterminacion.Anulada), (3, EstadoRedeterminacion.Calculada) },
            disparos.Select(d => (d.NroDisparo, d.Estado)));
        // 1,1³ y no 1,1⁴: el anulado no entra a la cadena.
        Assert.Equal(1.331000m, disparos.Last().VariacionAcumulada);
    }

    [Fact]
    public async Task DarDeBajaUnoIntermedio_Rechaza()
    {
        var (obraId, _, servicio) = await CrearDisparosAsync(2052, "PUB_A3_3", 2);
        var primero = (await DisparosAsync(obraId)).Single(d => d.NroDisparo == 1);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            GuardarDatosAdminAsync(servicio, primero.Id, a => a.Baja = EstadoRedeterminacion.Rechazada));

        Assert.Contains("Solo se puede rechazar el último disparo vigente", ex.Message);
        Assert.Equal(EstadoRedeterminacion.Calculada, (await DisparosAsync(obraId)).Single(d => d.NroDisparo == 1).Estado);
    }

    [Fact]
    public async Task Reactivar_ConSuNumeroLibreLoReencadena_YConElNumeroOcupadoRechaza()
    {
        var (obraId, tablaId, servicio) = await CrearDisparosAsync(2053, "PUB_A3_4", 2);
        var guardados = await DisparosAsync(obraId);
        var (primero, segundo) = (guardados[0], guardados[1]);

        // Con el 2 anulado, el 1 es el último vigente y se recalcula a +21% (mes 1 → 3).
        await GuardarDatosAdminAsync(servicio, segundo.Id, a => a.Baja = EstadoRedeterminacion.Anulada);
        await servicio.RecalcularDisparoAsync(primero.Id, obraId, tablaId,
            await servicio.CalcularAsync(obraId, tablaId, 2053, 1, "PUB_A3_4", 2053, 3, "PUB_A3_4"));

        // Reactivado, el 2 vuelve a la cadena sobre el coeficiente nuevo del 1.
        await GuardarDatosAdminAsync(servicio, segundo.Id, a =>
        {
            Assert.Equal(EstadoRedeterminacion.Anulada, a.Baja);
            a.Baja = null;
        });
        var reactivado = (await DisparosAsync(obraId)).Single(x => x.Id == segundo.Id);
        Assert.Equal(EstadoRedeterminacion.Calculada, reactivado.Estado);
        Assert.Equal(1.331000m, reactivado.VariacionAcumulada); // 1,21 × 1,1

        // Anulado otra vez y guardado otro N° 2, el viejo ya no puede volver.
        await GuardarDatosAdminAsync(servicio, segundo.Id, a => a.Baja = EstadoRedeterminacion.Anulada);
        await servicio.GuardarDisparoAsync(obraId, tablaId,
            await servicio.CalcularAsync(obraId, tablaId, 2053, 2, "PUB_A3_4", 2053, 3, "PUB_A3_4"));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            GuardarDatosAdminAsync(servicio, segundo.Id, a => a.Baja = null));
        Assert.Contains("otro disparo vigente con el número 2", ex.Message);
    }

    [Fact]
    public async Task Recalcular_ConElTokenDeUnaPaginaVieja_DaConflicto()
    {
        var (obraId, tablaId, servicio) = await CrearDisparosAsync(2054, "PUB_A3_5", 1);
        var id = (await DisparosAsync(obraId)).Single().Id;
        var paginaDeCalcular = (await servicio.GetDisparoParaRecalcularAsync(id))!;
        Assert.NotEmpty(paginaDeCalcular.RowVersion);

        // Con Calcular abierto, otra pestaña edita los datos administrativos (sin cambiar el
        // estado). El recálculo da los mismos valores y aun así tiene que detectarlo.
        await GuardarDatosAdminAsync(servicio, id, a => a.FechaLimitePresentacion = new DateTime(2026, 12, 31));
        var vm = await servicio.CalcularAsync(obraId, tablaId, 2054, 1, "PUB_A3_5", 2054, 2, "PUB_A3_5");

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            servicio.RecalcularDisparoAsync(id, obraId, tablaId, vm, paginaDeCalcular.RowVersion));

        var recargada = (await servicio.GetDisparoParaRecalcularAsync(id))!;
        await servicio.RecalcularDisparoAsync(id, obraId, tablaId, vm, recargada.RowVersion);
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

    [Fact]
    public async Task SaltoAnteriorALaBase_NoSeCalculaNiSeGuarda()
    {
        // Base dic / salto jun calculaba el coeficiente invertido y lo encadenaba.
        var (obraId, tablaId) = await CrearObraConTablaAsync();
        await CargarValoresAsync(
            (1, 2058, 6, 100m, null), (2, 2058, 6, 100m, null), (1, 2058, 12, 120m, null), (2, 2058, 12, 120m, null));
        var servicio = Servicio(Roles.Admin);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            servicio.CalcularAsync(obraId, tablaId, 2058, 12, null, 2058, 6, null));
        Assert.Contains("posterior al mes base", ex.Message);

        // El VM viene de la página: el guardado vuelve a exigirlo.
        var vm = await servicio.CalcularAsync(obraId, tablaId, 2058, 6, null, 2058, 12, null);
        (vm.AnioBase, vm.MesBase, vm.AnioSalto, vm.MesSalto) = (2058, 12, 2058, 6);
        ex = await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.GuardarDisparoAsync(obraId, tablaId, vm));
        Assert.Contains("posterior al mes base", ex.Message);

        await using var db = fx.CrearContexto();
        Assert.False(await db.RedeterminacionesGuardadas.AnyAsync(r => r.ObraId == obraId));
    }
}
