using System.Net;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
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
    [InlineData("/Certificado/Excel/1")]
    public async Task TodoLoDemas_ExigeAutenticacion(string url)
    {
        using var client = CrearCliente();

        using var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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
