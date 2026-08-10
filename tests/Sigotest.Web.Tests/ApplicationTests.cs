using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;

namespace Sigotest.Web.Tests;

public sealed class ApplicationTests : IClassFixture<SigotestWebApplicationFactory>
{
    private readonly SigotestWebApplicationFactory _factory;

    public ApplicationTests(SigotestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_ReturnsExpectedSafePayload()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var response = await client.GetAsync("/health");
        var rawContent = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK, rawContent);
        var payload = await response.Content.ReadFromJsonAsync<HealthPayload>();
        Assert.NotNull(payload);
        Assert.Equal("ok", payload.Status);
        Assert.Equal("Sigotest", payload.Application);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.False(string.IsNullOrWhiteSpace(payload.Machine));
        Assert.False(string.IsNullOrWhiteSpace(payload.Framework));
        Assert.DoesNotContain("ConnectionStrings", rawContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", rawContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Home_RequiresAuthentication()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Privacy_RequiresAuthentication()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var response = await client.GetAsync("/Privacy");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Error_IsAnonymous()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/Error");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void ApplicationServices_CanBeConstructed()
    {
        using var scope = _factory.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>());
    }

    private sealed record HealthPayload(string Status, string Application, string Machine, string Framework, DateTimeOffset Utc);
}

public sealed class SigotestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureTestServices(services =>
        {
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, _ => { });
        });
    }
}

public sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "IntegrationTest";

    public TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
        Task.FromResult(AuthenticateResult.NoResult());
}
