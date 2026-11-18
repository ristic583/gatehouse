extern alias GatehouseApi;

using System.Net;
using System.Text;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gatehouse.IntegrationTests.Security;

/// <summary>Security regression (M11, area 5): the API host started with <c>ASPNETCORE_ENVIRONMENT=Production</c>.</summary>
public sealed class ProductionModeTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string SecretDetail = "connection to secret-db-host:5432 failed";

    private readonly GatehouseApiFactory _api = new(postgres);

    public ValueTask InitializeAsync() => _api.InitializeAsync();

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    [Fact]
    public async Task Dev_routes_are_absent_outside_development()
    {
        using var production = Production();
        using var mia = CreateClient(production, DemoUser.Mia);

        using var response = await mia.PostAsync(new Uri("/api/v1/dev/reset-demo-access", UriKind.Relative), null, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(ApiRouteCatalog.Routes(production.Services), r => r.Pattern.StartsWith("/api/v1/dev/", StringComparison.Ordinal));
        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, $"{(int)response.StatusCode}");
    }

    [Fact]
    public void Dev_routes_stay_absent_in_production_when_the_flag_is_set()
    {
        using var production = Production(b => b.UseSetting(GatehouseApi::Gatehouse.Api.Features.Dev.DevEndpoints.EnableResetSetting, "true"));

        Assert.DoesNotContain(ApiRouteCatalog.Routes(production.Services), r => r.Pattern.StartsWith("/api/v1/dev/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unhandled_error_is_a_problem_without_exception_details()
    {
        var ct = TestContext.Current.CancellationToken;
        using var production = Production(b => b.ConfigureTestServices(s =>
            s.Replace(ServiceDescriptor.Scoped<Application.Dashboard.GetDashboardHandler>(_ => throw new InvalidOperationException(SecretDetail)))));
        using var mia = CreateClient(production, DemoUser.Mia);

        using var response = await mia.GetAsync(new Uri("/api/v1/dashboard", UriKind.Relative), ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("server.unexpected", body, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretDetail, body, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
        Assert.DoesNotContain(" at Gatehouse.", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Malformed_body_is_400_without_exception_details()
    {
        var ct = TestContext.Current.CancellationToken;
        using var production = Production();
        using var mia = CreateClient(production, DemoUser.Mia);
        using var content = new StringContent("{\"name\": ", Encoding.UTF8, "application/json");

        using var response = await mia.PostAsync(new Uri("/api/v1/admin/sites", UriKind.Relative), content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Api_responses_carry_security_headers()
    {
        using var production = Production();
        using var mia = CreateClient(production, DemoUser.Mia);

        using var response = await mia.GetAsync(new Uri("/api/v1/sites", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.True(response.Headers.Contains("X-Frame-Options") || response.Headers.Contains("Content-Security-Policy"));
    }

    [Fact]
    public async Task Https_responses_carry_hsts()
    {
        using var production = Production();
        using var mia = CreateClient(production, DemoUser.Mia, new Uri("https://gatehouse.example"));

        using var response = await mia.GetAsync(new Uri("/api/v1/sites", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.True(response.Headers.Contains("Strict-Transport-Security"));
    }

    private WebApplicationFactory<GatehouseApi::Program> Production(Action<IWebHostBuilder>? configure = null) =>
        _api.WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production");
            configure?.Invoke(b);
        });

    private static HttpClient CreateClient(WebApplicationFactory<GatehouseApi::Program> factory, DemoUser user, Uri? baseAddress = null)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = baseAddress ?? new Uri("http://localhost") });
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, user.Subject);
        client.DefaultRequestHeaders.Add(TestAuthHandler.EmailHeader, user.Email);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, user.Tenant);
        return client;
    }
}
