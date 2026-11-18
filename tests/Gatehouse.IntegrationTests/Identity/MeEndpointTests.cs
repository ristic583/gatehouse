using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gatehouse.Application.Identity;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Identity;

public sealed class MeEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly Uri Me = new("/api/v1/me", UriKind.Relative);

    private readonly GatehouseApiFactory _factory = new(postgres);

    public ValueTask InitializeAsync() => _factory.InitializeAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Anonymous_request_returns_401()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(Me, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_subject_email_and_tenant()
    {
        using var client = _factory.CreateClientFor("sub-anna", "anna.larsen@nordlys.test", "nordlys");

        var me = await client.GetFromJsonAsync<MeDto>(Me, TestContext.Current.CancellationToken);

        Assert.NotNull(me);
        Assert.Equal("sub-anna", me.Subject);
        Assert.Equal("anna.larsen@nordlys.test", me.Email);
        Assert.Equal("anna.larsen@nordlys.test", me.DisplayName);
        Assert.Equal("nordlys", me.TenantId);
        Assert.Equal("Nordlys A/S", me.TenantName);
    }

    [Fact]
    public async Task Missing_tenant_claim_returns_403_with_code()
    {
        using var client = _factory.CreateClientFor("sub-x", "x@nowhere.test", tenant: null);

        await AssertForbiddenWithCodeAsync(client, "tenant.missing_claim");
    }

    [Fact]
    public async Task Unknown_tenant_returns_403_with_code()
    {
        using var client = _factory.CreateClientFor("sub-x", "x@nowhere.test", "nowhere");

        await AssertForbiddenWithCodeAsync(client, "tenant.unknown");
    }

    [Fact]
    public async Task Suspended_tenant_returns_403_with_code()
    {
        using var client = _factory.CreateClientFor("sub-x", "x@frozen.test", "frozen");

        await AssertForbiddenWithCodeAsync(client, "tenant.inactive");
    }

    [Fact]
    public async Task Tenant_header_in_request_is_ignored()
    {
        using var client = _factory.CreateClientFor("sub-anna", "anna.larsen@nordlys.test", "nordlys");
        client.DefaultRequestHeaders.Add("X-Tenant", "bricks");

        var me = await client.GetFromJsonAsync<MeDto>(new Uri("/api/v1/me?tenant=bricks", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.NotNull(me);
        Assert.Equal("nordlys", me.TenantId);
    }

    [Fact]
    public async Task First_sign_in_links_person_by_email()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClientFor(DemoUser.Mikkel);

        var first = await client.GetFromJsonAsync<MeDto>(Me, ct);
        var second = await client.GetFromJsonAsync<MeDto>(Me, ct);

        Assert.NotNull(first?.Person);
        Assert.Equal("Mikkel Holm", first.Person.DisplayName);
        Assert.Equal(first.Person.PersonId, second?.Person?.PersonId);

        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var mikkel = await db.Persons.AsNoTracking().SingleAsync(p => p.Email == DemoUser.Mikkel.Email, ct);
        Assert.Equal(DemoUser.Mikkel.Subject, mikkel.IdentitySubject);
    }

    [Fact]
    public async Task Unknown_email_has_no_person_and_no_roles()
    {
        using var client = _factory.CreateClientFor("test-nobody", "nobody@nordlys.test", "nordlys");

        var me = await client.GetFromJsonAsync<MeDto>(Me, TestContext.Current.CancellationToken);

        Assert.NotNull(me);
        Assert.Null(me.Person);
        Assert.Empty(me.Roles);
    }

    [Fact]
    public async Task Anna_has_employee_role_only()
    {
        using var client = _factory.CreateClientFor(DemoUser.Anna);

        var me = await client.GetFromJsonAsync<MeDto>(Me, TestContext.Current.CancellationToken);

        Assert.NotNull(me?.Person);
        Assert.Equal("Employee", me.Person.Type);
        Assert.Contains("Employee", me.Roles);

        // The M2-08 seed makes Anna the sponsor of the generated visitors, so the derived Sponsor role is expected too;
        // she holds no manager, owner, staff or admin role.
        Assert.Empty(me.Roles.Except(["Employee", "Sponsor"]));
    }

    [Fact]
    public async Task Freja_is_tenant_admin_but_not_area_owner()
    {
        using var client = _factory.CreateClientFor(DemoUser.Freja);

        var me = await client.GetFromJsonAsync<MeDto>(Me, TestContext.Current.CancellationToken);

        Assert.NotNull(me);
        Assert.Contains("TenantAdmin", me.Roles);
        Assert.DoesNotContain("AreaOwner", me.Roles);
    }

    private static async Task AssertForbiddenWithCodeAsync(HttpClient client, string code)
    {
        using var response = await client.GetAsync(Me, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(code, body.GetProperty("code").GetString());
    }
}
