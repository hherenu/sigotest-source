using Microsoft.EntityFrameworkCore;
using Radzen;
using SIGO.Components.Shared;
using SIGO.Models;
using SIGO.Models.ViewModels;
using SIGO.Services;

namespace SIGO.Tests;

/// <summary>
/// Alta, edición y baja de ítems de estructura contra LocalDB: el servicio valida el padre
/// (agrupador de la misma estructura, sin ciclos) y el rango de los números aunque la página
/// ya los acote, y la baja de un agrupador deja a sus hijos en la raíz.
/// </summary>
public class EstructuraServiceTests(LocalDbFixture fx) : IClassFixture<LocalDbFixture>
{
    private EstructuraService Servicio() => new(new TestDbFactory(fx.Options), new FakeCurrentUser(Roles.CCyR));

    private sealed record Escenario(int EstructuraId, int RubroId, int SubRubroId, int ItemId, int AgrupadorAjenoId);

    /// <summary>Estructura con Rubro › Sub-rubro › Ítem, y otra estructura de la misma obra con su propio rubro.</summary>
    private async Task<Escenario> CrearAsync()
    {
        await using var db = fx.CrearContexto();
        var obra = new Obra { Nombre = $"Obra est {Guid.NewGuid():N}", NumeroLicitacion = "LP EST" };
        db.Obras.Add(obra);
        await db.SaveChangesAsync();

        var est = new EstructuraCostos { ObraId = obra.Id, Nombre = "Básico" };
        var otra = new EstructuraCostos { ObraId = obra.Id, Nombre = "BED N°1" };
        db.EstructurasCostos.AddRange(est, otra);
        await db.SaveChangesAsync();

        var rubro = new ItemEstructura { EstructuraCostosId = est.Id, EsAgrupador = true, Orden = 1, Descripcion = "Rubro" };
        var ajeno = new ItemEstructura { EstructuraCostosId = otra.Id, EsAgrupador = true, Orden = 1, Descripcion = "Rubro del BED" };
        db.ItemsEstructura.AddRange(rubro, ajeno);
        await db.SaveChangesAsync();

        var sub = new ItemEstructura { EstructuraCostosId = est.Id, EsAgrupador = true, Orden = 2, Descripcion = "Sub-rubro", AgrupadorPadreId = rubro.Id };
        db.ItemsEstructura.Add(sub);
        await db.SaveChangesAsync();

        var item = new ItemEstructura
        {
            EstructuraCostosId = est.Id, Orden = 3, Codigo = "1.1", Descripcion = "Ítem",
            AgrupadorPadreId = sub.Id, Cantidad = 2, PUBasico = 5, Monto = 10
        };
        db.ItemsEstructura.Add(item);
        await db.SaveChangesAsync();

        return new Escenario(est.Id, rubro.Id, sub.Id, item.Id, ajeno.Id);
    }

    private async Task<ItemEstructuraVM> VmAsync(int itemId) => (await Servicio().ObtenerItemAsync(itemId))!.Item;

    [Fact]
    public async Task AgregarItem_ConPadreDeOtraEstructura_Rechaza()
    {
        var e = await CrearAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Servicio().AgregarItemAsync(e.EstructuraId,
            new ItemEstructuraVM { Descripcion = "Nuevo", AgrupadorPadreId = e.AgrupadorAjenoId, Cantidad = 1, PUBasico = 1 }));

        Assert.Contains("no pertenece", ex.Message);
        await using var db = fx.CrearContexto();
        Assert.Equal(3, await db.ItemsEstructura.CountAsync(i => i.EstructuraCostosId == e.EstructuraId));
    }

    [Fact]
    public async Task AgregarItem_ConUnItemHojaComoPadre_Rechaza()
    {
        var e = await CrearAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Servicio().AgregarItemAsync(e.EstructuraId,
            new ItemEstructuraVM { Descripcion = "Nuevo", AgrupadorPadreId = e.ItemId }));

        Assert.Contains("no es un agrupador", ex.Message);
    }

    [Fact]
    public async Task AgregarItem_ConPadreValido_QuedaAlFinalDebajoDelPadre()
    {
        var e = await CrearAsync();

        await Servicio().AgregarItemAsync(e.EstructuraId,
            new ItemEstructuraVM { Descripcion = " Nuevo ", AgrupadorPadreId = e.SubRubroId, Cantidad = 3, PUBasico = 2 });

        await using var db = fx.CrearContexto();
        var nuevo = await db.ItemsEstructura.SingleAsync(i => i.EstructuraCostosId == e.EstructuraId && i.Descripcion == "Nuevo");
        Assert.Equal(e.SubRubroId, nuevo.AgrupadorPadreId);
        Assert.Equal(4, nuevo.Orden);
        Assert.Equal(6m, nuevo.Monto);
    }

    [Fact]
    public async Task AgregarItem_ConValoresQueNoEntranEnLaBase_RechazaConMensajeDeNegocio()
    {
        var e = await CrearAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Servicio().AgregarItemAsync(e.EstructuraId,
            new ItemEstructuraVM { Descripcion = "Nuevo", Cantidad = 1e8m, PUBasico = 1e8m }));

        Assert.Contains("monto", ex.Message);
    }

    [Fact]
    public async Task AgregarYActualizarItem_ConValoresNegativos_Rechazan()
    {
        var e = await CrearAsync();

        var alta = await Assert.ThrowsAsync<InvalidOperationException>(() => Servicio().AgregarItemAsync(e.EstructuraId,
            new ItemEstructuraVM { Descripcion = "Bonificación", Cantidad = 1, PUBasico = -500 }));
        Assert.Contains("negativo", alta.Message);

        var item = await VmAsync(e.ItemId);
        item.Cantidad = -2;
        var edicion = await Assert.ThrowsAsync<InvalidOperationException>(() => Servicio().ActualizarItemAsync(item));
        Assert.Contains("negativa", edicion.Message);

        await using var db = fx.CrearContexto();
        Assert.Equal(2m, (await db.ItemsEstructura.SingleAsync(i => i.Id == e.ItemId)).Cantidad);
    }

    [Fact]
    public async Task AgregarItem_AltasSimultaneas_NoRepitenElOrden()
    {
        // Ocho altas a la vez sobre la misma estructura, por Persistencia como lo hace la
        // página: con la transacción serializable ninguna lee el mismo «último orden» que
        // otra. Las que pierden el bloqueo no se guardan y el usuario ve un mensaje para
        // reintentar (no el texto en inglés con que EF envuelve el deadlock).
        var e = await CrearAsync();
        var servicio = Servicio();

        var resultados = await Task.WhenAll(Enumerable.Range(1, 8).Select(i => Task.Run(async () =>
        {
            var notif = new NotificationService();
            var ok = await Persistencia.EjecutarAsync(() => servicio.AgregarItemAsync(e.EstructuraId,
                new ItemEstructuraVM { Descripcion = $"Simultáneo {i}", Cantidad = 1, PUBasico = 1 }), notif);
            return (Ok: ok, Aviso: notif.Messages.SingleOrDefault()?.Detail);
        })));

        await using var db = fx.CrearContexto();
        var ordenes = await db.ItemsEstructura.Where(i => i.EstructuraCostosId == e.EstructuraId).Select(i => i.Orden).ToListAsync();
        Assert.Contains(resultados, r => r.Ok);
        Assert.Equal(3 + resultados.Count(r => r.Ok), ordenes.Count);
        Assert.Equal(ordenes.Count, ordenes.Distinct().Count());
        Assert.All(resultados.Where(r => !r.Ok), r =>
            Assert.Equal("Otro usuario estaba guardando datos relacionados en el mismo momento. Volvé a intentar.", r.Aviso));
    }

    [Fact]
    public async Task ActualizarItem_RubroDebajoDeSuPropioSubRubro_RechazaElCiclo()
    {
        var e = await CrearAsync();
        var rubro = await VmAsync(e.RubroId);
        rubro.AgrupadorPadreId = e.SubRubroId;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Servicio().ActualizarItemAsync(rubro));

        Assert.Contains("ciclo", ex.Message);
        await using var db = fx.CrearContexto();
        Assert.Null((await db.ItemsEstructura.SingleAsync(i => i.Id == e.RubroId)).AgrupadorPadreId);
    }

    [Fact]
    public async Task ActualizarItem_CambioDePadreValido_SeGuarda()
    {
        var e = await CrearAsync();
        var item = await VmAsync(e.ItemId);
        item.AgrupadorPadreId = e.RubroId;
        item.Cantidad = 4;

        await Servicio().ActualizarItemAsync(item);

        await using var db = fx.CrearContexto();
        var guardado = await db.ItemsEstructura.SingleAsync(i => i.Id == e.ItemId);
        Assert.Equal(e.RubroId, guardado.AgrupadorPadreId);
        Assert.Equal(20m, guardado.Monto);
    }

    [Fact]
    public async Task EliminarItem_Agrupador_DejaASusHijosEnLaRaiz()
    {
        var e = await CrearAsync();
        var sub = await VmAsync(e.SubRubroId);

        Assert.True(await Servicio().EliminarItemAsync(sub.Id, sub.RowVersion));

        await using var db = fx.CrearContexto();
        Assert.Null((await db.ItemsEstructura.SingleAsync(i => i.Id == e.ItemId)).AgrupadorPadreId);
        Assert.False(await db.ItemsEstructura.AnyAsync(i => i.Id == e.SubRubroId));
    }

    [Fact]
    public async Task Crear_RecortaElNombre()
    {
        var e = await CrearAsync();
        int obraId;
        await using (var db = fx.CrearContexto())
            obraId = (await db.EstructurasCostos.SingleAsync(x => x.Id == e.EstructuraId)).ObraId;

        var id = await Servicio().CrearAsync(obraId, new EstructuraFormVM { Nombre = "  Adicional N°1  " });

        await using var db2 = fx.CrearContexto();
        Assert.Equal("Adicional N°1", (await db2.EstructurasCostos.SingleAsync(x => x.Id == id)).Nombre);
    }
}
