using Microsoft.EntityFrameworkCore;
using SIGO.Models;
using SIGO.Models.Enums;
using SIGO.Services;

namespace SIGO.Tests;

/// <summary>
/// Tests de integración (LocalDB real) de las queries de los ABM simples que no tenían
/// cobertura: dependencias de EliminarObra (subconsultas correlacionadas), proyección
/// del listado de usuarios y análisis de importación de índices (duplicados por
/// publicación, incluida la publicación null). Nacieron como sondas de la barrida de
/// limpieza 2026-08-05 y cubren parte del BAJO-7 (servicios ABM sin tests).
/// </summary>
public class ServiciosQueryTests(LocalDbFixture fixture) : IClassFixture<LocalDbFixture>
{
    private ObraService ObraSvc() => new(new TestDbFactory(fixture.Options), new FakeCurrentUser(Roles.Admin));
    private UsuarioService UsuarioSvc() => new(new TestDbFactory(fixture.Options), new FakeCurrentUser(Roles.Admin));
    private IndiceService IndiceSvc() => new(new TestDbFactory(fixture.Options), new FakeCurrentUser(Roles.Admin));

    [Fact]
    public async Task EliminarObra_ConDependencias_RechazaYSinDependencias_Elimina()
    {
        int conDepsId, limpiaId;
        await using (var db = fixture.CrearContexto())
        {
            var conDeps = new Obra { Nombre = "Sonda con deps", NumeroLicitacion = "S-1" };
            conDeps.EstructurasCostos.Add(new EstructuraCostos { Nombre = "E1" });
            conDeps.TablasPonderacion.Add(new TablaPonderacion { Nombre = "T1" });
            var limpia = new Obra { Nombre = "Sonda limpia", NumeroLicitacion = "S-2" };
            db.Obras.AddRange(conDeps, limpia);
            await db.SaveChangesAsync();
            conDepsId = conDeps.Id;
            limpiaId = limpia.Id;
        }

        // La proyección de subconsultas correlacionadas debe traducirse y contar bien.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ObraSvc().EliminarAsync(conDepsId, []));
        Assert.Contains("estructura", ex.Message, StringComparison.OrdinalIgnoreCase);

        byte[] rv;
        await using (var db = fixture.CrearContexto())
            rv = (await db.Obras.AsNoTracking().FirstAsync(o => o.Id == limpiaId)).RowVersion;
        Assert.True(await ObraSvc().EliminarAsync(limpiaId, rv));

        Assert.False(await ObraSvc().EliminarAsync(99999, []));
    }

    [Fact]
    public async Task ListarUsuarios_ProyeccionDirecta_TraeRolesYOrdenPorNombre()
    {
        await using (var db = fixture.CrearContexto())
        {
            db.Usuarios.Add(new Usuario
            {
                WindowsUser = @"SONDA\zeta",
                Nombre = "Zeta",
                Roles = [new UsuarioRol { Rol = RolUsuario.Gerente }]
            });
            db.Usuarios.Add(new Usuario
            {
                WindowsUser = @"SONDA\alfa",
                Nombre = "Alfa",
                Email = "alfa@test",
                Roles = [new UsuarioRol { Rol = RolUsuario.Admin }, new UsuarioRol { Rol = RolUsuario.Director }]
            });
            await db.SaveChangesAsync();
        }

        var lista = await UsuarioSvc().ListarAsync();
        var alfa = lista.First(u => u.WindowsUser == @"SONDA\alfa");
        var zeta = lista.First(u => u.WindowsUser == @"SONDA\zeta");

        Assert.True(lista.FindIndex(u => u.Id == alfa.Id) < lista.FindIndex(u => u.Id == zeta.Id)); // OrderBy(Nombre)
        Assert.Equal("alfa@test", alfa.Email);
        Assert.Equal(2, alfa.Roles.Count());
        Assert.Contains(RolUsuario.Admin, alfa.Roles);
        Assert.Equal([RolUsuario.Gerente], zeta.Roles);
        Assert.NotEmpty(alfa.RowVersion);
    }

    [Fact]
    public async Task AnalizarImportacion_HashSetPrecargado_DetectaDuplicadosPorPublicacion()
    {
        string codigo;
        int indiceId;
        await using (var db = fixture.CrearContexto())
        {
            var indice = await db.IndicesINDEC.AsNoTracking().OrderBy(i => i.Id).FirstAsync();
            codigo = indice.CodigoIndice;
            indiceId = indice.Id;
            db.ValoresIndice.Add(new ValorIndice { IndiceId = indiceId, Anio = 2030, Mes = 1, Valor = 100m, IdPublicacion = "INDEC_INFORMA_01_30" });
            db.ValoresIndice.Add(new ValorIndice { IndiceId = indiceId, Anio = 2030, Mes = 2, Valor = 101m, IdPublicacion = null });
            await db.SaveChangesAsync();
        }

        // Con publicación: duplica solo dentro de la misma publicación.
        var filas = new List<FilaImport>
        {
            new() { Codigo = codigo, Anio = 2030, Mes = 1, Valor = 100m }, // ya existe en esa pub
            new() { Codigo = codigo, Anio = 2030, Mes = 3, Valor = 102m }, // nueva
            new() { Codigo = "NO_EXISTE", Anio = 2030, Mes = 1, Valor = 1m }
        };
        await IndiceSvc().AnalizarImportacionAsync(filas, "INDEC_INFORMA_01_30");
        Assert.Equal(EstadoFilaImport.YaExiste, filas[0].Estado);
        Assert.Equal(EstadoFilaImport.Ok, filas[1].Estado);
        Assert.Equal(EstadoFilaImport.CodigoDesconocido, filas[2].Estado);

        // Sin publicación (pub == null se compara contra IdPublicacion NULL).
        var sinPub = new List<FilaImport>
        {
            new() { Codigo = codigo, Anio = 2030, Mes = 2, Valor = 101m }, // ya existe sin pub
            new() { Codigo = codigo, Anio = 2030, Mes = 1, Valor = 100m }  // existe pero EN una pub → Ok
        };
        await IndiceSvc().AnalizarImportacionAsync(sinPub, null);
        Assert.Equal(EstadoFilaImport.YaExiste, sinPub[0].Estado);
        Assert.Equal(EstadoFilaImport.Ok, sinPub[1].Estado);
    }
}
