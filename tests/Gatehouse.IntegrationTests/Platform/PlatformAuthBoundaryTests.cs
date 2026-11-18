extern alias GatehouseApi;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Gatehouse.Application.Identity.MagicLinks;
using Gatehouse.Application.Platform.Dtos;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Platform;
using Gatehouse.Infrastructure.Persistence.Catalog;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AuthenticationSetup = GatehouseApi::Gatehouse.Api.Auth.AuthenticationSetup;

namespace Gatehouse.IntegrationTests.Platform;

/// <summary>authorization.md §11 rule 1: platform and tenant principals never cross.</summary>
public sealed class PlatformAuthBoundaryTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly Uri PlatformMe = new("/api/v1/platform/me", UriKind.Relative);
    private static readonly Uri TenantMe = new("/api/v1/me", UriKind.Relative);
    private readonly GatehouseApiFactory _api = new(postgres);

    public ValueTask InitializeAsync() => _api.InitializeAsync();

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Platform_me_returns_operator()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"op-{Guid.NewGuid():N}@platform.test";
        await AddOperatorAsync(email, ct);
        var sub = $"kc-{Guid.NewGuid():N}";
        using var client = _api.CreatePlatformClient(sub, email.ToUpperInvariant());

        using var response = await client.GetAsync(PlatformMe, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = (await response.Content.ReadFromJsonAsync<PlatformOperatorDto>(ApiJson.Options, ct))!;
        Assert.Equal(email, me.Email);
        await using var scope = _api.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().PlatformOperators.AsNoTracking()
            .SingleAsync(o => o.Email == email, ct);
        Assert.Equal(sub, stored.Subject);

        // Linked by subject from now on, even if the email claim changes.
        using var again = _api.CreatePlatformClient(sub, "renamed@platform.test");
        using var second = await again.GetAsync(PlatformMe, ct);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    [Fact]
    public async Task Unknown_operator_is_403()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _api.CreatePlatformClient($"kc-{Guid.NewGuid():N}", $"nobody-{Guid.NewGuid():N}@platform.test");

        using var response = await client.GetAsync(PlatformMe, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("platform.operator_not_found", await response.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tenant_token_on_platform_route_is_401()
    {
        var ct = TestContext.Current.CancellationToken;
        // Even when the tenant user's email matches an operator, a tenant token never reaches a platform route.
        await AddOperatorAsync(DemoUser.Anna.Email, ct);
        using var client = _api.CreateClientFor(DemoUser.Anna);

        using var response = await client.GetAsync(PlatformMe, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Platform_token_on_tenant_route_is_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"op-{Guid.NewGuid():N}@platform.test";
        await AddOperatorAsync(email, ct);
        using var client = _api.CreatePlatformClient($"kc-{Guid.NewGuid():N}", email);

        using var response = await client.GetAsync(TenantMe, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Guest_token_on_platform_route_is_401()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var real = _api.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.PostConfigure<AuthenticationOptions>(o =>
            {
                o.DefaultScheme = AuthenticationSetup.DefaultScheme;
                o.DefaultAuthenticateScheme = AuthenticationSetup.DefaultScheme;
                o.DefaultChallengeScheme = AuthenticationSetup.DefaultScheme;
                o.DefaultForbidScheme = AuthenticationSetup.DefaultScheme;
            })));
        var now = DateTimeOffset.UtcNow;
        Person sponsor;
        await using (var scope = await _api.CreateTenantScopeAsync("nordlys"))
        {
            sponsor = await scope.ServiceProvider.GetRequiredService<TenantDbContext>().Persons.AsNoTracking()
                .SingleAsync(p => p.Email == DemoUser.Mikkel.Email, ct);
        }

        var guest = Person.Create(PersonSource.Local, PersonType.Guest, "Gus", "Guest", $"guest-{Guid.NewGuid():N}@x.test",
            Eligibility.Create(now.AddHours(-1), now.AddHours(12), "Europe/Copenhagen").Value, null, sponsor, now).Value;
        var token = real.Services.GetRequiredService<IGuestTokenIssuer>().Issue(guest, new TenantId("nordlys"), now);
        using var client = real.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        using var response = await client.GetAsync(PlatformMe, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task AddOperatorAsync(string email, CancellationToken ct)
    {
        await using var scope = _api.Services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        if (!await catalog.PlatformOperators.AnyAsync(o => o.Email == email, ct))
        {
            catalog.PlatformOperators.Add(PlatformOperator.Create(email, "Test operator", DateTimeOffset.UtcNow).Value);
            await catalog.SaveChangesAsync(ct);
        }
    }
}
