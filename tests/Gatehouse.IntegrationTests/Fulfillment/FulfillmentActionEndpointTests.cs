using System.Net;
using System.Net.Http.Json;
using Gatehouse.Application.Access.Dtos;
using Gatehouse.Application.Fulfillment.Dtos;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.Tasks;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Access;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Fulfillment;

public sealed class FulfillmentActionEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatehouseApiFactory _factory = new(postgres);

    public ValueTask InitializeAsync() => _factory.InitializeAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, Guid id, string action, object? body = null) =>
        client.PostAsJsonAsync(new Uri($"/api/v1/fulfillments/{id}/{action}", UriKind.Relative), body ?? new { }, TestContext.Current.CancellationToken);

    private async Task<(AccessRequestDto Request, Guid FulfillmentId)> ApprovedAsync(DemoUser requester, string area, DemoUser approver1, DemoUser? approver2, int startDays = 1)
    {
        using var requesterClient = _factory.CreateClientFor(requester);
        using var created = await AccessRequestEndpointTests.PostRequestAsync(_factory, requesterClient, "nordlys", area, startDays: startDays);
        var request = await AccessRequestEndpointTests.CreatedAsync(created);
        using var a1 = _factory.CreateClientFor(approver1);
        (await ApprovalEndpointTests.DecideAsync(a1, request.Steps[0].StepId, "approve")).Dispose();
        if (approver2 is not null)
        {
            using var a2 = _factory.CreateClientFor(approver2);
            (await ApprovalEndpointTests.DecideAsync(a2, request.Steps[1].StepId, "approve")).Dispose();
        }

        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var eid = new Gatehouse.Domain.Access.EntitlementId(request.EntitlementId);
        var f = await db.Fulfillments.AsNoTracking().SingleAsync(x => x.EntitlementId == eid, TestContext.Current.CancellationToken);
        return (request, f.Id.Value);
    }

    private Task<(AccessRequestDto Request, Guid FulfillmentId)> ServerRoomAsync(int startDays = 1) => ApprovedAsync(DemoUser.Anna, "CPH-SERVER", DemoUser.Mikkel, DemoUser.Sofie, startDays);

    private async Task<FloorAccessMapDto> MapAsync(HttpClient client)
    {
        var floorId = await AccessMapEndpointTests.FloorIdAsync(_factory, "nordlys", "CPH-HQ", 2);
        return (await client.GetFromJsonAsync<FloorAccessMapDto>(new Uri($"/api/v1/floors/{floorId}/access-map", UriKind.Relative), TestContext.Current.CancellationToken))!;
    }

    [Fact]
    public async Task Staff_confirm_turns_area_green()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, fid) = await ServerRoomAsync(0);
        using var jonas = _factory.CreateClientFor(DemoUser.Jonas);
        using var response = await PostAsync(jonas, fid, "confirm");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var anna = _factory.CreateClientFor(DemoUser.Anna);
        var server = (await MapAsync(anna)).Areas.Single(a => a.Code == "CPH-SERVER");
        Assert.Equal(("HasAccess", "Confirmed"), (server.State, server.Reason));

        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var id = new FulfillmentId(fid);
        var row = await db.Fulfillments.AsNoTracking().SingleAsync(f => f.Id == id, ct);
        var jonasId = (await db.Persons.SingleAsync(p => p.Email == DemoUser.Jonas.Email, ct)).Id.Value.ToString();
        Assert.Equal(ConfirmerKind.Staff, row.ConfirmedByKind);
        Assert.Equal(jonasId, row.ConfirmedById);
        Assert.NotNull(row.ConfirmedAt);
    }

    [Fact]
    public async Task Start_then_confirm_passes_through_pending()
    {
        var (_, fid) = await ServerRoomAsync();
        using var jonas = _factory.CreateClientFor(DemoUser.Jonas);
        using var started = await PostAsync(jonas, fid, "start");
        Assert.Equal("Pending", (await started.Content.ReadFromJsonAsync<FulfillmentDetailDto>(TestContext.Current.CancellationToken))!.Item.Status);
        using var confirmed = await PostAsync(jonas, fid, "confirm");
        Assert.Equal("Confirmed", (await confirmed.Content.ReadFromJsonAsync<FulfillmentDetailDto>(TestContext.Current.CancellationToken))!.Item.Status);
    }

    [Fact]
    public async Task Fail_requires_reason()
    {
        var (_, fid) = await ServerRoomAsync();
        using var jonas = _factory.CreateClientFor(DemoUser.Jonas);
        using var response = await PostAsync(jonas, fid, "fail", new FailFulfillmentDto("  "));
        await AccessRequestEndpointTests.AssertProblemAsync(response, HttpStatusCode.BadRequest, "fulfillment.reason_required");
    }

    [Fact]
    public async Task Fail_opens_task_and_area_stays_pending()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, fid) = await ServerRoomAsync();
        using var jonas = _factory.CreateClientFor(DemoUser.Jonas);
        using var response = await PostAsync(jonas, fid, "fail", new FailFulfillmentDto("badge printer offline"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _factory.DrainMessagesAsync("nordlys");

        using var anna = _factory.CreateClientFor(DemoUser.Anna);
        var server = (await MapAsync(anna)).Areas.Single(a => a.Code == "CPH-SERVER");
        Assert.Equal(("Pending", "AwaitingFulfillment"), (server.State, server.Reason));

        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var task = await db.StaffTasks.AsNoTracking().SingleAsync(t => t.SubjectId == fid && t.Kind == StaffTaskKind.FulfillmentFailed, ct);
        Assert.Equal(StaffTaskStatus.Open, task.Status);
        var site = await db.Sites.SingleAsync(s => s.Code == "CPH-HQ", ct);
        Assert.Equal(site.Id, task.SiteId);
    }

    private async Task SetConnectorModeAsync()
    {
        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        foreach (var instance in await db.PacsInstances.ToListAsync(TestContext.Current.CancellationToken))
        {
            instance.SetFulfillmentMode(Gatehouse.Domain.Fulfillment.FulfillmentMode.Connector);
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Confirm_on_connector_instance_returns_409_connector_managed()
    {
        var (_, fid) = await ServerRoomAsync();
        await SetConnectorModeAsync();
        using var jonas = _factory.CreateClientFor(DemoUser.Jonas);
        using var response = await PostAsync(jonas, fid, "confirm");
        Assert.Equal(System.Net.HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("fulfillment.connector_managed", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Retry_on_connector_instance_is_allowed()
    {
        var (_, fid) = await ServerRoomAsync();
        using var jonas = _factory.CreateClientFor(DemoUser.Jonas);
        (await PostAsync(jonas, fid, "fail", new FailFulfillmentDto("x"))).Dispose();
        await SetConnectorModeAsync();
        using var retried = await PostAsync(jonas, fid, "retry");
        Assert.Equal(System.Net.HttpStatusCode.OK, retried.StatusCode);
    }

    [Fact]
    public async Task Retry_then_confirm_resolves_task()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, fid) = await ServerRoomAsync();
        using var jonas = _factory.CreateClientFor(DemoUser.Jonas);
        (await PostAsync(jonas, fid, "fail", new FailFulfillmentDto("x"))).Dispose();
        await _factory.DrainMessagesAsync("nordlys");
        using var retried = await PostAsync(jonas, fid, "retry");
        Assert.Equal(2, (await retried.Content.ReadFromJsonAsync<FulfillmentDetailDto>(ct))!.Item.Revision);
        (await PostAsync(jonas, fid, "confirm")).Dispose();
        await _factory.DrainMessagesAsync("nordlys");

        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var task = await db.StaffTasks.AsNoTracking().SingleAsync(t => t.SubjectId == fid, ct);
        Assert.Equal(StaffTaskStatus.Resolved, task.Status);
        Assert.Equal(StaffTaskResolutionAction.AutoResolved, task.ResolutionAction);
    }

    [Fact]
    public async Task Confirm_twice_is_409()
    {
        var (_, fid) = await ServerRoomAsync();
        using var jonas = _factory.CreateClientFor(DemoUser.Jonas);
        (await PostAsync(jonas, fid, "confirm")).Dispose();
        using var second = await PostAsync(jonas, fid, "confirm");
        await AccessRequestEndpointTests.AssertProblemAsync(second, HttpStatusCode.Conflict, "fulfillment.invalid_transition");
    }

    [Fact]
    public async Task Staff_cannot_handle_own_entitlement()
    {
        using var jonas = _factory.CreateClientFor(DemoUser.Jonas);
        using var created = await AccessRequestEndpointTests.PostRequestAsync(_factory, jonas, "nordlys", "CPH-LOBBY");
        var request = await AccessRequestEndpointTests.CreatedAsync(created);
        using var erik = _factory.CreateClientFor(DemoUser.Erik);
        foreach (var step in request.Steps.Where(s => s.Status == "Pending"))
        {
            (await ApprovalEndpointTests.DecideAsync(erik, step.StepId, "approve")).Dispose();
        }

        var list = await FulfillmentQueueEndpointTests.ListAsync(jonas, "open");
        var item = list.SingleOrDefault(i => i.EntitlementId == request.EntitlementId);
        Assert.NotNull(item);
        using var response = await PostAsync(jonas, item.FulfillmentId, "confirm");
        await AccessRequestEndpointTests.AssertProblemAsync(response, HttpStatusCode.Forbidden, "fulfillment.self_handling_forbidden");
    }

    [Fact]
    public async Task Tenant_admin_cannot_confirm()
    {
        var (_, fid) = await ServerRoomAsync();
        using var freja = _factory.CreateClientFor(DemoUser.Freja);
        using var response = await PostAsync(freja, fid, "confirm");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Confirm_is_audited()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, fid) = await ServerRoomAsync();
        using var jonas = _factory.CreateClientFor(DemoUser.Jonas);
        (await PostAsync(jonas, fid, "confirm")).Dispose();

        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var subject = fid.ToString();
        var entry = await db.AuditEntries.AsNoTracking().SingleAsync(e => e.Action == "fulfillment.confirmed" && e.SubjectId == subject, ct);
        var jonasId = (await db.Persons.SingleAsync(p => p.Email == DemoUser.Jonas.Email, ct)).Id.Value.ToString();
        Assert.Equal(jonasId, entry.ActorId);
    }
}
