using Microsoft.EntityFrameworkCore;
using SIGO.Models;
using SIGO.Models.ViewModels;
using SIGO.Services;

namespace SIGO.Tests;

/// <summary>
/// Prórrogas de plazo de la obra contra LocalDB real: correlativo, encadenado de fechas,
/// fin de contrato vigente en la obra, eliminación solo de la última y la ficha que no
/// pisa la fecha vigente cuando hay prórrogas.
/// </summary>
public class ObraProrrogaTests(LocalDbFixture fx) : IClassFixture<LocalDbFixture>
{
    private ObraService Svc() => new(new TestDbFactory(fx.Options), new FakeCurrentUser(Roles.Admin));

    private async Task<int> CrearObraAsync(DateTime? fechaFin)
    {
        await using var db = fx.CrearContexto();
        var obra = new Obra
        {
            Nombre = $"Obra prórroga {Guid.NewGuid():N}", NumeroLicitacion = "LP PRO",
            PresupuestoOficial = 1000m, FechaFinalContrato = fechaFin
        };
        db.Obras.Add(obra);
        await db.SaveChangesAsync();
        return obra.Id;
    }

    private async Task<Obra> ObraAsync(int id)
    {
        await using var db = fx.CrearContexto();
        return await db.Obras.AsNoTracking().Include(o => o.Prorrogas).SingleAsync(o => o.Id == id);
    }

    [Fact]
    public async Task Agregar_EncadenaFechasYActualizaElFinVigente()
    {
        var obraId = await CrearObraAsync(new DateTime(2030, 6, 30));
        var svc = Svc();

        await svc.AgregarProrrogaAsync(new NuevaProrrogaVM
        {
            ObraId = obraId, FechaFinNueva = new DateTime(2030, 9, 30, 15, 0, 0), IFActo = "  IF-2030-1  "
        });
        await svc.AgregarProrrogaAsync(new NuevaProrrogaVM { ObraId = obraId, FechaFinNueva = new DateTime(2031, 1, 31) });

        var obra = await ObraAsync(obraId);
        Assert.Equal(new DateTime(2031, 1, 31), obra.FechaFinalContrato); // vigente = última
        var prorrogas = obra.Prorrogas.OrderBy(p => p.Numero).ToList();
        Assert.Equal([1, 2], prorrogas.Select(p => p.Numero));
        Assert.Equal(new DateTime(2030, 6, 30), prorrogas[0].FechaFinAnterior);
        Assert.Equal(new DateTime(2030, 9, 30), prorrogas[0].FechaFinNueva); // normalizada al día
        Assert.Equal("IF-2030-1", prorrogas[0].IFActo);                     // recortado
        Assert.Equal(new DateTime(2030, 9, 30), prorrogas[1].FechaFinAnterior);
        Assert.Equal(new DateTime(2031, 1, 31), prorrogas[1].FechaFinNueva);

        // El detalle y el form ven las prórrogas.
        var detalle = await svc.ObtenerDetalleAsync(obraId);
        Assert.Equal(2, detalle!.Prorrogas.Count);
        Assert.True(detalle.TieneProrrogas);
        Assert.True((await svc.ObtenerAsync(obraId))!.TieneProrrogas);
    }

    [Fact]
    public async Task Agregar_SinFinVigenteOFechaNoPosterior_Rechaza()
    {
        var svc = Svc();

        var sinFin = await CrearObraAsync(null);
        var ex1 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.AgregarProrrogaAsync(new NuevaProrrogaVM { ObraId = sinFin, FechaFinNueva = new DateTime(2030, 1, 1) }));
        Assert.Contains("no tiene fecha de fin", ex1.Message);

        var obraId = await CrearObraAsync(new DateTime(2030, 6, 30));
        var ex2 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.AgregarProrrogaAsync(new NuevaProrrogaVM { ObraId = obraId, FechaFinNueva = new DateTime(2030, 6, 30) }));
        Assert.Contains("debe ser posterior", ex2.Message);
        var ex3 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.AgregarProrrogaAsync(new NuevaProrrogaVM { ObraId = obraId, FechaFinNueva = null }));
        Assert.Contains("obligatoria", ex3.Message);

        Assert.Equal(new DateTime(2030, 6, 30), (await ObraAsync(obraId)).FechaFinalContrato); // intacta
    }

    [Fact]
    public async Task Eliminar_SoloLaUltimaYRestauraLaFechaAnterior()
    {
        var obraId = await CrearObraAsync(new DateTime(2030, 6, 30));
        var svc = Svc();
        await svc.AgregarProrrogaAsync(new NuevaProrrogaVM { ObraId = obraId, FechaFinNueva = new DateTime(2030, 9, 30) });
        await svc.AgregarProrrogaAsync(new NuevaProrrogaVM { ObraId = obraId, FechaFinNueva = new DateTime(2031, 1, 31) });
        var prorrogas = (await ObraAsync(obraId)).Prorrogas.OrderBy(p => p.Numero).ToList();

        // La primera tiene posteriores: rechazo sin tocar nada.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.EliminarProrrogaAsync(prorrogas[0].Id));
        Assert.Contains("Solo se puede eliminar la última", ex.Message);
        Assert.Equal(new DateTime(2031, 1, 31), (await ObraAsync(obraId)).FechaFinalContrato);

        // La última: se elimina y la obra vuelve a la fecha anterior a ella.
        Assert.True(await svc.EliminarProrrogaAsync(prorrogas[1].Id));
        var obra = await ObraAsync(obraId);
        Assert.Equal(new DateTime(2030, 9, 30), obra.FechaFinalContrato);
        Assert.Single(obra.Prorrogas);

        // Ya eliminada: false (patrón "ya la borró otro usuario").
        Assert.False(await svc.EliminarProrrogaAsync(prorrogas[1].Id));

        // Sin prórrogas queda el fin original.
        Assert.True(await svc.EliminarProrrogaAsync(prorrogas[0].Id));
        Assert.Equal(new DateTime(2030, 6, 30), (await ObraAsync(obraId)).FechaFinalContrato);
    }

    [Fact]
    public async Task ActualizarFicha_ConProrrogas_ConservaElFinVigente()
    {
        var obraId = await CrearObraAsync(new DateTime(2030, 6, 30));
        var svc = Svc();
        await svc.AgregarProrrogaAsync(new NuevaProrrogaVM { ObraId = obraId, FechaFinNueva = new DateTime(2030, 9, 30) });

        // Un VM que llega con otra fecha de fin (el form la deshabilita, pero el servicio
        // es el espejo): se guarda el resto y la fecha vigente se conserva.
        var vm = (await svc.ObtenerAsync(obraId))!;
        vm.Contratista = "Contratista SA";
        vm.FechaFinalContrato = new DateTime(2029, 1, 1);
        await svc.ActualizarAsync(vm);

        var obra = await ObraAsync(obraId);
        Assert.Equal("Contratista SA", obra.Contratista);
        Assert.Equal(new DateTime(2030, 9, 30), obra.FechaFinalContrato);
    }
}
