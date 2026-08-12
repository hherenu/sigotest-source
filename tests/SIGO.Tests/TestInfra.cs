using Microsoft.EntityFrameworkCore;
using SIGO.Data;
using SIGO.Services;

// Los tests son Windows-only igual que la app (LocalDB + tipos marcados con
// SupportedOSPlatform en el ensamblado de SIGO): declararlo acalla los CA1416.
[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]

// Las clases que usan LocalDB se serializan para evitar saturar la instancia
// compartida de SQL Server durante su inicialización en GitHub Actions.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SIGO.Tests;

/// <summary>Usuario de prueba con los roles que se le pasen.</summary>
public sealed class FakeCurrentUser(params string[] roles) : ICurrentUser
{
    public string? UserId => @"TEST\tester";
    public bool IsInRole(string rol) => roles.Contains(rol);
}

/// <summary>
/// Resolver de emails de AD falso: devuelve <see cref="Email"/> para cualquier
/// cuenta (null = "AD sin mail") y registra las cuentas consultadas.
/// </summary>
public sealed class FakeDirectoryEmails(string? email = "tester@ad.test") : IDirectoryEmailResolver
{
    public string? Email { get; set; } = email;
    public List<string> Consultados { get; } = [];

    public string? EmailDe(string windowsUser)
    {
        Consultados.Add(windowsUser);
        return Email;
    }
}

/// <summary>Factory mínima sobre opciones fijas (los servicios reciben IDbContextFactory).</summary>
public sealed class TestDbFactory(DbContextOptions<AppDbContext> options, ICurrentUser? user = null)
    : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() => new(options, user);
}

/// <summary>
/// Base de datos LocalDB descartable por clase de test (EnsureCreated aplica el
/// modelo real: precisiones, índices únicos filtrados, rowversion y seeds del
/// catálogo). Se elimina al terminar la clase.
/// </summary>
public sealed class LocalDbFixture : IAsyncLifetime
{
    private readonly string _dbName = $"RedetTests_{Guid.NewGuid():N}";

    public DbContextOptions<AppDbContext> Options { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer($@"Server=(localdb)\mssqllocaldb;Database={_dbName};Trusted_Connection=True;MultipleActiveResultSets=true")
            .Options;

        await using var db = new AppDbContext(Options);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = new AppDbContext(Options);
        await db.Database.EnsureDeletedAsync();
    }

    public AppDbContext CrearContexto() => new(Options);
}
