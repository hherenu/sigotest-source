using Microsoft.EntityFrameworkCore;
using SIGO.Models;
using SIGO.Models.Enums;
using SIGO.Models.ViewModels;
using SIGO.Services;

namespace SIGO.Tests;

/// <summary>
/// Edición de usuarios contra LocalDB real — en particular la concurrencia entre
/// administradores cuando el cambio es SOLO de roles (sin AplicarTokenSesion no se
/// emitía UPDATE del padre y el token nunca se comparaba).
/// </summary>
public class UsuarioServiceTests(LocalDbFixture fx) : IClassFixture<LocalDbFixture>
{
    private UsuarioService Servicio() =>
        new(new TestDbFactory(fx.Options), new FakeCurrentUser(Roles.Admin));

    [Fact]
    public async Task Actualizar_SoloRoles_ConTokenViejo_DaConflicto()
    {
        // X tiene [Admin, Director]; existe otro Admin activo para que quitarle Admin
        // a X no choque con el invariante del último Admin.
        int xId; byte[] tokenViejo;
        await using (var db = fx.CrearContexto())
        {
            var otroAdmin = new Usuario { WindowsUser = $@"DOM\admin_{Guid.NewGuid():N}", Nombre = "Otro admin" };
            otroAdmin.Roles.Add(new UsuarioRol { Rol = RolUsuario.Admin });
            var x = new Usuario { WindowsUser = $@"DOM\x_{Guid.NewGuid():N}", Nombre = "X" };
            x.Roles.Add(new UsuarioRol { Rol = RolUsuario.Admin });
            x.Roles.Add(new UsuarioRol { Rol = RolUsuario.Director });
            db.Usuarios.AddRange(otroAdmin, x);
            await db.SaveChangesAsync();
            xId = x.Id;
            tokenViejo = x.RowVersion;
        }
        var servicio = Servicio();

        // Admin A le quita Director (cambio solo de roles): pasa, y bumpea el token.
        await servicio.ActualizarAsync(new UsuarioVM
        {
            Id = xId, Nombre = "X", Activo = true,
            Roles = [RolUsuario.Admin], RowVersion = tokenViejo
        });

        // Admin B, con la grilla vieja, le quita Admin: conflicto de concurrencia en
        // vez de dejar a X sin ningún rol en silencio (escenario M9, auditoría 07-31).
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            servicio.ActualizarAsync(new UsuarioVM
            {
                Id = xId, Nombre = "X", Activo = true,
                Roles = [RolUsuario.Director], RowVersion = tokenViejo
            }));

        await using var db2 = fx.CrearContexto();
        var roles = (await db2.Usuarios.Include(u => u.Roles).SingleAsync(u => u.Id == xId)).Roles;
        Assert.Equal(RolUsuario.Admin, Assert.Single(roles).Rol);
    }

    [Fact]
    public async Task Actualizar_NoTocaElEmail_QueVieneDeAd()
    {
        int id; byte[] token;
        await using (var db = fx.CrearContexto())
        {
            var u = new Usuario { WindowsUser = $@"DOM\mail_{Guid.NewGuid():N}", Nombre = "Mail", Email = "de-ad@test.local" };
            db.Usuarios.Add(u);
            await db.SaveChangesAsync();
            id = u.Id;
            token = u.RowVersion;
        }

        // Aunque el VM traiga otro valor (form viejo), el email asignado en el
        // auto-registro desde AD queda intacto: la edición no lo pisa.
        await Servicio().ActualizarAsync(new UsuarioVM
        {
            Id = id, Nombre = "Mail", Email = "tipeado@a-mano.test", Activo = true,
            Roles = [], RowVersion = token
        });
        await using var db2 = fx.CrearContexto();
        Assert.Equal("de-ad@test.local", (await db2.Usuarios.SingleAsync(u => u.Id == id)).Email);
    }
}
