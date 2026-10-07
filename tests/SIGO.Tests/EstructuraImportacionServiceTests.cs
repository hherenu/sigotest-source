using Microsoft.EntityFrameworkCore;
using SIGO.Models;
using SIGO.Models.Enums;
using SIGO.Services;
using SIGO.Services.Importacion;

namespace SIGO.Tests;

/// <summary>
/// Persistencia de la importación de ítems: modo Agregar (orden a continuación, padres
/// resueltos, agrupadores sin código), modo Reemplazar (borra la jerarquía existente;
/// rechazado si un certificado usa la estructura o un BED toma sus ítems como origen),
/// token de concurrencia, ítems vistos y rol.
/// </summary>
public class EstructuraImportacionServiceTests(LocalDbFixture fx) : IClassFixture<LocalDbFixture>
{
    private EstructuraService Servicio(params string[] roles) =>
        new(new TestDbFactory(fx.Options), new FakeCurrentUser(roles));

    /// <summary>Obra con la estructura «Básico»: un rubro y un ítem P.1 debajo.</summary>
    private async Task<(int ObraId, int EstructuraId, byte[] RowVersion)> CrearEstructuraAsync()
    {
        await using var db = fx.CrearContexto();
        var obra = new Obra { Nombre = $"Obra import {Guid.NewGuid():N}", NumeroLicitacion = "LP IMP" };
        db.Obras.Add(obra);
        await db.SaveChangesAsync();

        var est = new EstructuraCostos { ObraId = obra.Id, Nombre = "Básico" };
        db.EstructurasCostos.Add(est);
        await db.SaveChangesAsync();

        var rubro = new ItemEstructura { EstructuraCostosId = est.Id, EsAgrupador = true, Orden = 1, Descripcion = "Rubro previo" };
        db.ItemsEstructura.Add(rubro);
        await db.SaveChangesAsync();
        db.ItemsEstructura.Add(new ItemEstructura
        {
            EstructuraCostosId = est.Id, Orden = 2, Codigo = "P.1", Descripcion = "Ítem previo",
            AgrupadorPadreId = rubro.Id, Cantidad = 1, PUBasico = 10, Monto = 10
        });
        await db.SaveChangesAsync();

        var rowVersion = (await db.EstructurasCostos.AsNoTracking().SingleAsync(e => e.Id == est.Id)).RowVersion;
        return (obra.Id, est.Id, rowVersion);
    }

    /// <summary>Ids de los ítems de la estructura, como los ve la página al abrirse.</summary>
    private async Task<List<int>> ItemsAsync(int estructuraId)
    {
        await using var db = fx.CrearContexto();
        return await db.ItemsEstructura.Where(i => i.EstructuraCostosId == estructuraId).Select(i => i.Id).ToListAsync();
    }

    /// <summary>Importa como la página: con los ítems que tiene la estructura en este momento.</summary>
    private async Task<int> ImportarAsync(int estructuraId, IReadOnlyList<FilaEstructuraImport> filas,
        EstructuraService.ModoImportacion modo, byte[] rowVersion, string rol = Roles.CCyR) =>
        await Servicio(rol).ImportarItemsAsync(estructuraId, filas, modo, rowVersion, await ItemsAsync(estructuraId));

    /// <summary>Rubro R › (Ítem I1, Sub-rubro S › Ítem I2) + una fila ignorada en el medio.</summary>
    private static List<FilaEstructuraImport> Filas() =>
    [
        new() { Fila = 10, Tipo = TipoFilaEstructura.Rubro, Codigo = "R", Descripcion = " Rubro importado " },
        new() { Fila = 11, Tipo = TipoFilaEstructura.Item, Codigo = "R.1", Descripcion = "Ítem 1", Unidad = "m2", Cantidad = 2.5m, PUBasico = 4m, PadreIndice = 0 },
        new() { Fila = 12, Tipo = TipoFilaEstructura.Ignorada, Descripcion = "Separador" },
        new() { Fila = 13, Tipo = TipoFilaEstructura.SubRubro, Descripcion = "Sub-rubro", PadreIndice = 0 },
        new() { Fila = 14, Tipo = TipoFilaEstructura.ItemSinMonto, Codigo = "R.2", Descripcion = "Ítem sin monto", Unidad = "gl", PadreIndice = 3, TipoMovimiento = TipoMovimiento.Adicional }
    ];

    [Fact]
    public async Task Agregar_ConservaLosExistentesYResuelveLosPadres()
    {
        var (_, estId, rv) = await CrearEstructuraAsync();

        var creadas = await ImportarAsync(estId, Filas(), EstructuraService.ModoImportacion.Agregar, rv);

        Assert.Equal(4, creadas);
        await using var db = fx.CrearContexto();
        var items = await db.ItemsEstructura.Where(i => i.EstructuraCostosId == estId).OrderBy(i => i.Orden).ToListAsync();
        Assert.Equal(6, items.Count);
        Assert.Equal("Ítem previo", items[1].Descripcion);

        var rubro = items[2];
        Assert.True(rubro.EsAgrupador);
        Assert.Null(rubro.Codigo);                       // los agrupadores no llevan código en la app
        Assert.Equal("Rubro importado", rubro.Descripcion);
        Assert.Null(rubro.AgrupadorPadreId);
        Assert.Equal(3, rubro.Orden);

        var item1 = items[3];
        Assert.Equal(rubro.Id, item1.AgrupadorPadreId);
        Assert.Equal(10m, item1.Monto);
        Assert.Equal(TipoMovimiento.Normal, item1.TipoMovimiento);

        var sub = items[4];
        Assert.True(sub.EsAgrupador);
        Assert.Equal(rubro.Id, sub.AgrupadorPadreId);

        var item2 = items[5];
        Assert.Equal(sub.Id, item2.AgrupadorPadreId);
        Assert.Null(item2.Monto);
        Assert.Equal(TipoMovimiento.Adicional, item2.TipoMovimiento);
    }

    [Fact]
    public async Task ItemCuyoPadreNoSeImporta_QuedaEnLaRaiz()
    {
        // El parser no produce un hijo de una fila que no se importa, pero el servicio no lo
        // supone: el ítem va a la raíz en vez de fallar o colgar de otro.
        var (_, estId, rv) = await CrearEstructuraAsync();
        List<FilaEstructuraImport> filas =
        [
            new() { Fila = 1, Tipo = TipoFilaEstructura.Ignorada, Codigo = "R", Descripcion = "Rubro sin ítems en el grupo" },
            new() { Fila = 2, Tipo = TipoFilaEstructura.Item, Codigo = "R.1", Descripcion = "Ítem", Cantidad = 1, PUBasico = 1, PadreIndice = 0 }
        ];

        Assert.Equal(1, await ImportarAsync(estId, filas, EstructuraService.ModoImportacion.Agregar, rv));

        await using var db = fx.CrearContexto();
        var item = await db.ItemsEstructura.SingleAsync(i => i.EstructuraCostosId == estId && i.Codigo == "R.1");
        Assert.Null(item.AgrupadorPadreId);
    }

    [Fact]
    public async Task Reemplazar_BorraLaJerarquiaExistente()
    {
        var (_, estId, rv) = await CrearEstructuraAsync();

        await ImportarAsync(estId, Filas(), EstructuraService.ModoImportacion.Reemplazar, rv, Roles.Admin);

        await using var db = fx.CrearContexto();
        var items = await db.ItemsEstructura.Where(i => i.EstructuraCostosId == estId).OrderBy(i => i.Orden).ToListAsync();
        Assert.Equal(4, items.Count);
        Assert.DoesNotContain(items, i => i.Descripcion == "Ítem previo");
        Assert.Equal([1, 2, 3, 4], items.Select(i => i.Orden));
    }

    [Fact]
    public async Task Reemplazar_ConBloqueDeCertificadoSinPorcentajes_SeRechazaSinTocarNada()
    {
        // Un borrador con el bloque recién agregado no tiene filas de ItemsCertificado:
        // igual usa la estructura y el reemplazo debe rechazarse.
        var (obraId, estId, rv) = await CrearEstructuraAsync();
        await using (var db = fx.CrearContexto())
        {
            var cert = new Certificado { ObraId = obraId, Numero = 1, Mes = 1, Anio = 2030 };
            db.Certificados.Add(cert);
            await db.SaveChangesAsync();
            db.CertificadoEstructuras.Add(new CertificadoEstructura { CertificadoId = cert.Id, EstructuraCostosId = estId, Orden = 1 });
            await db.SaveChangesAsync();
        }

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ImportarAsync(estId, Filas(), EstructuraService.ModoImportacion.Reemplazar, rv));

        Assert.Contains("certificados", ex.Message);
        Assert.Equal(2, (await ItemsAsync(estId)).Count);
    }

    [Fact]
    public async Task Reemplazar_SiUnBedTomaSusItemsComoOrigen_SeRechazaSinTocarNada()
    {
        var (obraId, estId, rv) = await CrearEstructuraAsync();
        await using (var db = fx.CrearContexto())
        {
            var origen = await db.ItemsEstructura.SingleAsync(i => i.EstructuraCostosId == estId && i.Codigo == "P.1");
            var bed = new EstructuraCostos { ObraId = obraId, Nombre = "BED N°1" };
            db.EstructurasCostos.Add(bed);
            await db.SaveChangesAsync();
            db.ItemsEstructura.Add(new ItemEstructura
            {
                EstructuraCostosId = bed.Id, Orden = 1, Codigo = "P.1", Descripcion = "Demasía del ítem previo",
                TipoMovimiento = TipoMovimiento.Demasia, ItemOrigenId = origen.Id
            });
            await db.SaveChangesAsync();
        }

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ImportarAsync(estId, Filas(), EstructuraService.ModoImportacion.Reemplazar, rv));

        Assert.Contains("BED", ex.Message);
        Assert.Equal(2, (await ItemsAsync(estId)).Count);
    }

    [Fact]
    public async Task Reemplazar_SiOtroUsuarioAgregoItems_Conflicto()
    {
        var (_, estId, rv) = await CrearEstructuraAsync();
        var vistos = await ItemsAsync(estId);
        await using (var db = fx.CrearContexto())
        {
            // Otro usuario agrega un ítem: el RowVersion de la estructura no cambia.
            db.ItemsEstructura.Add(new ItemEstructura { EstructuraCostosId = estId, Orden = 3, Codigo = "P.2", Descripcion = "Agregado por otro" });
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            Servicio(Roles.CCyR).ImportarItemsAsync(estId, Filas(), EstructuraService.ModoImportacion.Reemplazar, rv, vistos));

        Assert.Equal(3, (await ItemsAsync(estId)).Count);
    }

    [Fact]
    public async Task TokenDeConcurrenciaViejo_Conflicto()
    {
        var (_, estId, rv) = await CrearEstructuraAsync();
        await using (var db = fx.CrearContexto())
        {
            var est = await db.EstructurasCostos.SingleAsync(e => e.Id == estId);
            est.Nombre = "Renombrada por otro usuario";
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            ImportarAsync(estId, Filas(), EstructuraService.ModoImportacion.Agregar, rv));
    }

    [Fact]
    public async Task SinRolDelModulo_SeRechaza()
    {
        var (_, estId, rv) = await CrearEstructuraAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ImportarAsync(estId, Filas(), EstructuraService.ModoImportacion.Agregar, rv, Roles.Director));
    }

    [Fact]
    public async Task SinFilasImportables_SeRechaza()
    {
        var (_, estId, rv) = await CrearEstructuraAsync();
        var soloIgnoradas = new List<FilaEstructuraImport> { new() { Tipo = TipoFilaEstructura.Ignorada, Descripcion = "x" } };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ImportarAsync(estId, soloIgnoradas, EstructuraService.ModoImportacion.Agregar, rv));
    }
}
