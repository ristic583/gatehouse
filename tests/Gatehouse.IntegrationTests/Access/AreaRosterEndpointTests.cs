using System.Net;
using System.Net.Http.Json;
using Gatehouse.Application.Access.Dtos;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Access;

public sealed class AreaRosterEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatehouseApiFactory _factory = new(postgres);

    public ValueTask InitializeAsync() => _factory.InitializeAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private async Task<Guid> AreaIdAsync(string code)
    {
        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        return (await db.Areas.SingleAsync(a => a.Code == code, TestContext.Current.CancellationToken)).Id.Value;
    }

    private async Task<HttpResponseMessage> GetAsync(DemoUser user, Guid areaId)
    {
        using var client = _factory.CreateClientFor(user);
        return await client.GetAsync(new Uri($"/api/v1/areas/{areaId}/roster", UriKind.Relative), TestContext.Current.CancellationToken);
    }

    private static async Task<AreaRosterDto> RosterAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AreaRosterDto>(TestContext.Current.CancellationToken))!;
    }

    [Fact]
    public async Task Owner_sees_active_holders_and_can_revoke()
    {
        var roster = await RosterAsync(await GetAsync(DemoUser.Erik, await AreaIdAsync("CPH-OFFICE-2")));
        Assert.True(roster.CanRevoke);
        Assert.Contains(roster.Entries, e => e.Subject.DisplayName.StartsWith("Anna", StringComparison.Ordinal) && e.Status == "Approved");
    }

    [Fact]
    public async Task Staff_sees_roster_and_can_emergency_revoke()
    {
        var roster = await RosterAsync(await GetAsync(DemoUser.Jonas, await AreaIdAsync("CPH-OFFICE-2")));
        Assert.True(roster.CanEmergencyRevoke);
        Assert.False(roster.CanRevoke);
        Assert.NotEmpty(roster.Entries);
    }

    [Fact]
    public async Task Employee_gets_403()
    {
        using var response = await GetAsync(DemoUser.Anna, await AreaIdAsync("CPH-OFFICE-2"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Pending_revocation_stays_on_roster_until_confirmed()
    {
        var areaId = await AreaIdAsync("CPH-OFFICE-2");
        using var erik = _factory.CreateClientFor(DemoUser.Erik);
        using var jonas = _factory.CreateClientFor(DemoUser.Jonas);
        var before = await RosterAsync(await GetAsync(DemoUser.Erik, areaId));
        var anna = before.Entries.Single(e => e.Subject.DisplayName.StartsWith("Anna", StringComparison.Ordinal));
        using var revoked = await erik.PostAsJsonAsync(new Uri($"/api/v1/access-requests/{anna.EntitlementId}/revoke", UriKind.Relative), new RevokeEntitlementDto("Left"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);

        var pending = (await RosterAsync(await GetAsync(DemoUser.Erik, areaId))).Entries.Single(e => e.EntitlementId == anna.EntitlementId);
        Assert.Equal("Revoked", pending.Status);
        Assert.NotNull(pending.RevokedAt);

        var open = await Fulfillment.FulfillmentQueueEndpointTests.ListAsync(jonas, "open");
        var revoke = open.Single(i => i.EntitlementId == anna.EntitlementId && i.Kind == "Revoke");
        (await jonas.PostAsJsonAsync(new Uri($"/api/v1/fulfillments/{revoke.FulfillmentId}/confirm", UriKind.Relative), new { }, TestContext.Current.CancellationToken)).Dispose();

        var after = await RosterAsync(await GetAsync(DemoUser.Erik, areaId));
        Assert.DoesNotContain(after.Entries, e => e.EntitlementId == anna.EntitlementId);
    }
}
