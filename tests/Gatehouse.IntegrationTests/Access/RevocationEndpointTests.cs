using System.Net;
using System.Net.Http.Json;
using Gatehouse.Application.Access.Dtos;
using Gatehouse.Application.Fulfillment.Dtos;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Fulfillment;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Access;

public sealed class RevocationEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatehouseApiFactory _factory = new(postgres);

    public ValueTask InitializeAsync() => _factory.InitializeAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private async Task<Guid> SeededAsync(string areaCode)
    {
        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var ct = TestContext.Current.CancellationToken;
        var annaId = (await db.Persons.SingleAsync(p => p.Email == DemoUser.Anna.Email, ct)).Id;
        var areaId = (await db.Areas.SingleAsync(a => a.Code == areaCode, ct)).Id;
        return (await db.Entitlements.AsNoTracking().SingleAsync(e => e.PersonId == annaId && e.AreaId == areaId, ct)).Id.Value;
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, Guid id, string action, object? body = null) =>
        client.PostAsJsonAsync(new Uri($"/api/v1/access-requests/{id}/{action}", UriKind.Relative), body ?? new { }, TestContext.Current.CancellationToken);

    private static async Task<AccessRequestDto> DtoAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AccessRequestDto>(TestContext.Current.CancellationToken))!;
    }

    private async Task<FloorAccessMapDto> MapAsync(HttpClient client, int level)
    {
        var floorId = await AccessMapEndpointTests.FloorIdAsync(_factory, "nordlys", "CPH-HQ", level);
        return (await client.GetFromJsonAsync<FloorAccessMapDto>(new Uri($"/api/v1/floors/{floorId}/access-map", UriKind.Relative), TestContext.Current.CancellationToken))!;
    }

    [Fact]
    public async Task Owner_revoke_requires_reason()
    {
        var id = await SeededAsync("CPH-OFFICE-2");
        using var erik = _factory.CreateClientFor(DemoUser.Erik);
        using var response = await PostAsync(erik, id, "revoke", new RevokeEntitlementDto(" "));
        await AccessRequestEndpointTests.AssertProblemAsync(response, HttpStatusCode.BadRequest, "entitlement.revocation_reason_required");
    }

    [Fact]
    public async Task Owner_revoke_stays_pending_until_confirmed()
    {
        var id = await SeededAsync("CPH-OFFICE-2");
        using var erik = _factory.CreateClientFor(DemoUser.Erik);
        using var jonas = _factory.CreateClientFor(DemoUser.Jonas);
        using var anna = _factory.CreateClientFor(DemoUser.Anna);
        var dto = await DtoAsync(await PostAsync(erik, id, "revoke", new RevokeEntitlementDto("Left the project")));
        Assert.Equal("Revoked", dto.Status);
        Assert.Equal("Desired", dto.RevocationFulfillmentStatus);
        Assert.Equal("Normal", dto.Revocation!.Kind);

        var office = (await MapAsync(anna, 2)).Areas.Single(a => a.Code == "CPH-OFFICE-2");
        Assert.Equal(("Pending", "RevocationPending"), (office.State, office.Reason));

        var open = await FulfillmentQueueEndpointTests.ListAsync(jonas, "open");
        var revoke = open.Single(i => i.EntitlementId == id && i.Kind == "Revoke");
        using var confirmed = await jonas.PostAsJsonAsync(new Uri($"/api/v1/fulfillments/{revoke.FulfillmentId}/confirm", UriKind.Relative), new { }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);

        office = (await MapAsync(anna, 2)).Areas.Single(a => a.Code == "CPH-OFFICE-2");
        Assert.Equal(("NoAccess", "NoEntitlement"), (office.State, office.Reason));
    }

    [Fact]
    public async Task Manager_who_is_not_owner_cannot_revoke()
    {
        var id = await SeededAsync("CPH-OFFICE-2");
        using var mikkel = _factory.CreateClientFor(DemoUser.Mikkel);
        using var response = await PostAsync(mikkel, id, "revoke", new RevokeEntitlementDto("x"));
        await AccessRequestEndpointTests.AssertProblemAsync(response, HttpStatusCode.Forbidden, "entitlement.revoke_forbidden");
    }

    [Fact]
    public async Task Tenant_admin_cannot_revoke()
    {
        var id = await SeededAsync("CPH-OFFICE-2");
        using var freja = _factory.CreateClientFor(DemoUser.Freja);
        using var response = await PostAsync(freja, id, "revoke", new RevokeEntitlementDto("x"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Emergency_revoke_is_first_in_open_queue()
    {
        var office1 = await SeededAsync("CPH-OFFICE-1");
        var lobby = await SeededAsync("CPH-LOBBY");
        using var erik = _factory.CreateClientFor(DemoUser.Erik);
        using var jonas = _factory.CreateClientFor(DemoUser.Jonas);
        (await PostAsync(erik, office1, "revoke", new RevokeEntitlementDto("done"))).Dispose();
        var dto = await DtoAsync(await PostAsync(jonas, lobby, "emergency-revoke", new RevokeEntitlementDto("Lost badge")));
        Assert.Equal("Emergency", dto.Revocation!.Kind);

        var open = await FulfillmentQueueEndpointTests.ListAsync(jonas, "open");
        Assert.Equal(("Revoke", "High", lobby), (open[0].Kind, open[0].Priority, open[0].EntitlementId));
    }

    [Fact]
    public async Task Revoke_supersedes_unapplied_grant()
    {
        using var anna = _factory.CreateClientFor(DemoUser.Anna);
        using var mikkel = _factory.CreateClientFor(DemoUser.Mikkel);
        using var sofie = _factory.CreateClientFor(DemoUser.Sofie);
        using var created = await AccessRequestEndpointTests.PostRequestAsync(_factory, anna, "nordlys", "CPH-SERVER");
        var request = await AccessRequestEndpointTests.CreatedAsync(created);
        (await ApprovalEndpointTests.DecideAsync(mikkel, request.Steps[0].StepId, "approve")).Dispose();
        (await ApprovalEndpointTests.DecideAsync(sofie, request.Steps[1].StepId, "approve")).Dispose();

        var dto = await DtoAsync(await PostAsync(sofie, request.EntitlementId, "revoke", new RevokeEntitlementDto("Changed mind")));
        Assert.Null(dto.RevocationFulfillmentStatus);

        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var eid = new Gatehouse.Domain.Access.EntitlementId(request.EntitlementId);
        var rows = await db.Fulfillments.AsNoTracking().Where(f => f.EntitlementId == eid).ToListAsync(TestContext.Current.CancellationToken);
        Assert.All(rows, f => Assert.Equal((FulfillmentKind.Grant, FulfillmentStatus.Superseded), (f.Kind, f.Status)));

        var server = (await MapAsync(anna, 2)).Areas.Single(a => a.Code == "CPH-SERVER");
        Assert.Equal("NoAccess", server.State);
    }

    [Fact]
    public async Task Requester_cancels_pending_request()
    {
        var id = await SeededAsync("CPH-LAB");
        using var anna = _factory.CreateClientFor(DemoUser.Anna);
        using var sofie = _factory.CreateClientFor(DemoUser.Sofie);
        var dto = await DtoAsync(await PostAsync(anna, id, "cancel"));
        Assert.Equal("Cancelled", dto.Status);
        Assert.DoesNotContain(await ApprovalEndpointTests.InboxAsync(sofie), i => i.EntitlementId == id);
    }

    [Fact]
    public async Task Only_requester_can_cancel()
    {
        var id = await SeededAsync("CPH-VAULT");
        using var mikkel = _factory.CreateClientFor(DemoUser.Mikkel);
        using var response = await PostAsync(mikkel, id, "cancel");
        await AccessRequestEndpointTests.AssertProblemAsync(response, HttpStatusCode.Forbidden, "entitlement.cancel_forbidden");
    }

    [Fact]
    public async Task Revoke_is_audited()
    {
        var id = await SeededAsync("CPH-OFFICE-2");
        using var erik = _factory.CreateClientFor(DemoUser.Erik);
        (await PostAsync(erik, id, "revoke", new RevokeEntitlementDto("Left"))).Dispose();

        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        Assert.True(await db.AuditEntries.AnyAsync(a => a.Action == "entitlement.revoked" && a.SubjectId == id.ToString(), TestContext.Current.CancellationToken));
    }
}
