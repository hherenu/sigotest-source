using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SIGO.Tests;

/// <summary>
/// Contratos de infraestructura del sitio (sigotest-source): los únicos endpoints
/// anónimos son GET /health (prueba de vida sin datos sensibles) y /Error; todo lo
/// demás exige usuario autenticado vía la FallbackPolicy. La app bootea en el entorno
/// "Testing" sin base de datos: las migraciones se omiten y las tareas de fondo
/// toleran el fallo de conexión (se loguea y reintenta).
/// </summary>
public sealed class ContractTests : IClassFixture<SigoWebApplicationFactory>
{
    private readonly SigoWebApplicationFactory _factory;

    public ContractTests(SigoWebApplicationFactory factory) => _factory = factory;

    private HttpClient CrearCliente() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false
    });

    [Fact]
    public async Task Health_EsAnonimoYNoExponeDatosSensibles()
    {
        using var client = CrearCliente();

        using var response = await client.GetAsync("/health");
        var crudo = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK, crudo);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var payload = await response.Content.ReadFromJsonAsync<HealthPayload>();
        Assert.NotNull(payload);
        Assert.Equal("ok", payload.Status);
        Assert.Equal("SIGO", payload.Application);
        Assert.False(string.IsNullOrWhiteSpace(payload.Machine));
        Assert.False(string.IsNullOrWhiteSpace(payload.Framework));
        Assert.DoesNotContain("ConnectionStrings", crudo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", crudo, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Error_EsAnonimo()
    {
        using var client = CrearCliente();

        using var response = await client.GetAsync("/Error");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/obras")]
    [InlineData("/Certificado/DescargarExcel/1")]   // la ruta real del controller (antes una inexistente)
    [InlineData("/Indices/DescargarPlantilla?idPublicacion=INDEC_INFORMA_07_26")]
    public async Task TodoLoDemas_ExigeAutenticacion(string url)
    {
        using var client = CrearCliente();

        using var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void Cultura_EsArgentinaEnTodoElProceso()
    {
        // Arranca la app si ningún otro test de la clase lo hizo.
        _ = _factory.Services;

        // Si no, RadzenNumeric, los DatePicker y {0:N2} usan la cultura del pool de IIS.
        Assert.Equal("es-AR", CultureInfo.DefaultThreadCurrentCulture?.Name);
        Assert.Equal("es-AR", CultureInfo.DefaultThreadCurrentUICulture?.Name);
    }

    [Fact]
    public void CorreoConUnValorImposible_LaAppNoArranca()
    {
        using var conDiaInexistente = _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["Correo:DiaDigest"] = "31" })));

        var error = Record.Exception(() => conDiaInexistente.CreateClient());

        // El motivo queda en la excepción del arranque (y en el log del servidor).
        Assert.NotNull(error);
        var validacion = ExcepcionesDe(error).OfType<OptionsValidationException>().FirstOrDefault();
        Assert.NotNull(validacion);
        Assert.Contains(validacion.Failures, f => f.StartsWith("Correo:DiaDigest", StringComparison.Ordinal));
    }

    private static IEnumerable<Exception> ExcepcionesDe(Exception e)
    {
        yield return e;
        IEnumerable<Exception> internas = e is AggregateException a ? a.InnerExceptions : [];
        if (e is not AggregateException && e.InnerException is not null)
            internas = [e.InnerException];
        foreach (var interna in internas)
            foreach (var x in ExcepcionesDe(interna))
                yield return x;
    }

    private sealed record HealthPayload(string Status, string Application, string Machine, string Framework, DateTimeOffset Utc);
}

/// <summary>
/// Boot de la app real en entorno "Testing": sin Negotiate (se instala un esquema que
/// no autentica a nadie, para que la FallbackPolicy responda 401) y sin base de datos.
/// </summary>
public sealed class SigoWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = EsquemaAnonimo.Nombre;
                options.DefaultChallengeScheme = EsquemaAnonimo.Nombre;
            }).AddScheme<AuthenticationSchemeOptions, EsquemaAnonimo>(EsquemaAnonimo.Nombre, _ => { });
        });
    }
}

/// <summary>Esquema que nunca autentica: cada request llega como anónimo puro.</summary>
public sealed class EsquemaAnonimo(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Nombre = "TestAnonimo";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
        Task.FromResult(AuthenticateResult.NoResult());
}
