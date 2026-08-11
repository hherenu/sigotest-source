using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using SIGO.Models.Enums;
using SIGO.Services;

namespace SIGO.Tests;

/// <summary>
/// Auto-registro y bootstrap de Admin contra LocalDB real. La fixture es propia de
/// esta clase, así que la tabla Usuarios arranca VACÍA (condición del bootstrap).
/// </summary>
public class RolesClaimsTransformationTests(LocalDbFixture fx) : IClassFixture<LocalDbFixture>
{
    private sealed class EnvStub : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "SIGO.Tests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private RolesClaimsTransformation Crear() => new(
        new TestDbFactory(fx.Options),
        new ConfigurationBuilder().Build(),
        new EnvStub(),
        new FakeDirectoryEmails("nuevo@ad.test"),
        NullLogger<RolesClaimsTransformation>.Instance);

    private static ClaimsPrincipal Principal(string name) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, name)], "TestAuth"));

    [Fact]
    public async Task Bootstrap_SoloElPrimerIngresanteQuedaComoAdmin()
    {
        // Primer ingreso con tabla vacía: Admin persistido + opera como Admin ya en
        // este request (claim del bootstrap, atado al insert efectivo).
        var primero = await Crear().TransformAsync(Principal(@"DOM\primero"));
        Assert.True(primero.IsInRole(Roles.Admin));

        // Segundo ingreso (otra identidad): fila auto-creada SIN roles y sin claim.
        var segundo = await Crear().TransformAsync(Principal(@"DOM\segundo"));
        Assert.False(segundo.IsInRole(Roles.Admin));

        await using var db = fx.CrearContexto();
        var admin = await db.Usuarios.Include(u => u.Roles).SingleAsync(u => u.WindowsUser == @"DOM\primero");
        Assert.Single(admin.Roles, r => r.Rol == RolUsuario.Admin);
        var sinRoles = await db.Usuarios.Include(u => u.Roles).SingleAsync(u => u.WindowsUser == @"DOM\segundo");
        Assert.True(sinRoles.Activo);
        Assert.Empty(sinRoles.Roles);

        // El auto-registro trae el email de AD (no hay carga manual).
        Assert.Equal("nuevo@ad.test", admin.Email);
        Assert.Equal("nuevo@ad.test", sinRoles.Email);
    }
}
