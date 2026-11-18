using System.Net.Http.Json;
using System.Text.Json;
using Gatehouse.Agent.Transport;
using Gatehouse.Application.Access.Dtos;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Adapters;
using Gatehouse.Connector.Contracts.Events;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Access;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Connectors.Flow;

public sealed class AgentFlowTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Tenant = "nordlys";

    private readonly GatehouseApiFactory _api = new(postgres);
    private GatewayFactory _gateway = null!;
    private AgentHarness? _agent;

    public async ValueTask InitializeAsync()
    {
        await _api.InitializeAsync();
        _gateway = new GatewayFactory(postgres, _api.CatalogConnectionString);
        await _gateway.InitializeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _agent?.Dispose();
        await _gateway.DisposeAsync();
        await _api.DisposeAsync();
    }

    [Fact]
    public async Task Approved_request_reaches_confirmed_without_staff()
    {
        await StartAgentAsync();
        await _agent!.HeartbeatAsync();
        var fid = await ApprovedAsync();
        await _api.DrainMessagesAsync(Tenant);
        await _agent.RunCyclesAsync(3);
        await _api.DrainMessagesAsync(Tenant);

        Assert.Equal(["UpsertCardholder", "GrantAccess"], _agent.Adapter.Calls);
        var fulfillment = await FulfillmentAsync(fid);
        Assert.Equal(FulfillmentStatus.Confirmed, fulfillment.Status);
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        Assert.False(await scope.ServiceProvider.GetRequiredService<TenantDbContext>().StaffTasks.AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Command_delivered_twice_is_applied_once()
    {
        await StartAgentAsync();
        await _agent!.HeartbeatAsync();
        var fid = await ApprovedAsync();
        await _api.DrainMessagesAsync(Tenant);
        await _agent.RunCyclesAsync(1); // Upsert applied and acked
        _agent.DropAcks = true;
        await _agent.RunCyclesAsync(1); // Grant applied, ack lost
        Assert.Equal(1, _agent.Adapter.Calls.Count(c => c == "GrantAccess"));
        foreach (var ack in await _agent.Pending.ListAsync(10, TestContext.Current.CancellationToken))
        {
            await _agent.Pending.RemoveAsync(ack.CommandId, TestContext.Current.CancellationToken);
        }

        _agent.DropAcks = false;
        await ExpireLeasesAsync();
        await _agent.RunCyclesAsync(1); // redelivered, answered from processed_commands
        await _api.DrainMessagesAsync(Tenant);

        Assert.Equal(1, _agent.Adapter.Calls.Count(c => c == "GrantAccess"));
        Assert.Equal(FulfillmentStatus.Confirmed, (await FulfillmentAsync(fid)).Status);
        Assert.Equal(2, (await CommandsAsync()).Single(c => c.Type == ConnectorCommandTypes.GrantAccess).Attempts);
    }

    [Fact]
    public async Task Offline_agent_leaves_area_unknown_then_applies_in_order()
    {
        await StartAgentAsync();
        var fid = await ApprovedAsync();
        await _api.DrainMessagesAsync(Tenant);
        Assert.Equal(("Unknown", "ConnectorOffline"), await ServerRoomStateAsync());
        Assert.All(await CommandsAsync(), c => Assert.Equal(ConnectorCommandStatus.Queued, c.Status));

        await _agent!.HeartbeatAsync();
        await _agent.RunCyclesAsync(3);
        await _api.DrainMessagesAsync(Tenant);

        Assert.Equal(["UpsertCardholder", "GrantAccess"], _agent.Adapter.Calls);
        Assert.Equal(FulfillmentStatus.Confirmed, (await FulfillmentAsync(fid)).Status);
        Assert.NotEqual("Unknown", (await ServerRoomStateAsync()).State); // online again; the grant itself starts later
    }

    [Fact]
    public async Task Pacs_failure_ends_failed_with_task()
    {
        await StartAgentAsync();
        _agent!.Adapter.GrantResult = Gatehouse.Connector.Contracts.Adapters.AdapterResult.Failed("pacs.rejected", "no", false);
        await _agent.HeartbeatAsync();
        var fid = await ApprovedAsync();
        await _api.DrainMessagesAsync(Tenant);
        await _agent.RunCyclesAsync(3);
        await _api.DrainMessagesAsync(Tenant);

        Assert.Equal(FulfillmentStatus.Failed, (await FulfillmentAsync(fid)).Status);
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        Assert.True(await scope.ServiceProvider.GetRequiredService<TenantDbContext>().StaffTasks.AnyAsync(t => t.SubjectId == fid, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Emergency_revoke_jumps_the_queue()
    {
        await StartAgentAsync();
        await _agent!.HeartbeatAsync();
        await ApprovedAsync();
        await _api.DrainMessagesAsync(Tenant);
        using var jonas = _api.CreateClientFor(DemoUser.Jonas);
        var entitlementId = await EntitlementAsync("CPH-LOBBY");
        using var response = await jonas.PostAsJsonAsync(new Uri($"/api/v1/access-requests/{entitlementId}/emergency-revoke", UriKind.Relative), new RevokeEntitlementDto("Lost badge"), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        await _api.DrainMessagesAsync(Tenant);

        await _agent.RunCyclesAsync(1);

        Assert.Equal("RevokeAccess", _agent.Adapter.Calls[0]);
    }

    private async Task StartAgentAsync()
    {
        await SetModeAsync(FulfillmentMode.Connector);
        var cert = await ConnectorTestEnrollment.EnrollAsync(_gateway, Tenant, "HQ-Copenhagen");
        _agent = new AgentHarness(_gateway, cert, new FakePacsAdapter());
    }

    private async Task<(string State, string Reason)> ServerRoomStateAsync()
    {
        using var anna = _api.CreateClientFor(DemoUser.Anna);
        var floorId = await AccessMapEndpointTests.FloorIdAsync(_api, Tenant, "CPH-HQ", 2);
        var map = await anna.GetFromJsonAsync<FloorAccessMapDto>(new Uri($"/api/v1/floors/{floorId}/access-map", UriKind.Relative), TestContext.Current.CancellationToken);
        var area = map!.Areas.Single(a => a.Code == "CPH-SERVER");
        return (area.State.ToString(), area.Reason.ToString());
    }

    [Fact]
    public async Task Door_events_from_adapter_reach_outbox()
    {
        var ct = TestContext.Current.CancellationToken;
        await StartAgentAsync();
        var now = DateTimeOffset.UtcNow;
        var events = Enumerable.Range(0, 3)
            .Select(i => new PacsDoorEvent(Guid.NewGuid(), now.AddSeconds(i), DoorEventType.AccessGranted, "D-CPH-SERVER", null, "4000000001", "P-1", ReaderDirection.In))
            .ToList();
        events.ForEach(_agent!.Adapter.Events.Enqueue);

        Assert.Equal(3, await _agent!.Pump.PumpOnceAsync(ct));
        Assert.Equal(PollCycleOutcome.Continue, await _agent.Send.RunOnceAsync(ct));

        Assert.Equal(0, await _agent.Events.CountAsync(ct));
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var payloads = await scope.ServiceProvider.GetRequiredService<TenantDbContext>().OutboxMessages.AsNoTracking()
            .Where(m => m.Type == "DoorEventsReceived").Select(m => m.Payload).ToListAsync(ct);
        var received = payloads
            .SelectMany(p => JsonDocument.Parse(p).RootElement.GetProperty("events").EnumerateArray())
            .Select(e => e.GetProperty("eventId").GetGuid())
            .ToHashSet();
        Assert.All(events, e => Assert.Contains(e.EventId, received));
    }

    private async Task ExpireLeasesAsync()
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        await scope.ServiceProvider.GetRequiredService<TenantDbContext>().ConnectorCommands
            .Where(c => c.Status == ConnectorCommandStatus.Delivered)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.LeaseUntil, DateTimeOffset.UtcNow.AddMinutes(-1)), TestContext.Current.CancellationToken);
    }

    private async Task<List<ConnectorCommand>> CommandsAsync()
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        return await scope.ServiceProvider.GetRequiredService<TenantDbContext>().ConnectorCommands.AsNoTracking().OrderBy(c => c.Sequence).ToListAsync(TestContext.Current.CancellationToken);
    }

    private async Task<Gatehouse.Domain.Fulfillment.Fulfillment> FulfillmentAsync(Guid fid)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        return await scope.ServiceProvider.GetRequiredService<TenantDbContext>().Fulfillments.AsNoTracking().SingleAsync(f => f.Id == new FulfillmentId(fid), TestContext.Current.CancellationToken);
    }

    private async Task SetModeAsync(FulfillmentMode mode)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        foreach (var instance in await db.PacsInstances.ToListAsync(TestContext.Current.CancellationToken))
        {
            instance.SetFulfillmentMode(mode);
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<Guid> EntitlementAsync(string areaCode)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var ct = TestContext.Current.CancellationToken;
        var annaId = (await db.Persons.SingleAsync(p => p.Email == DemoUser.Anna.Email, ct)).Id;
        var areaId = (await db.Areas.SingleAsync(a => a.Code == areaCode, ct)).Id;
        return (await db.Entitlements.AsNoTracking().SingleAsync(e => e.PersonId == annaId && e.AreaId == areaId, ct)).Id.Value;
    }

    private async Task<Guid> ApprovedAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        using var anna = _api.CreateClientFor(DemoUser.Anna);
        using var mikkel = _api.CreateClientFor(DemoUser.Mikkel);
        using var sofie = _api.CreateClientFor(DemoUser.Sofie);
        using var created = await AccessRequestEndpointTests.PostRequestAsync(_api, anna, Tenant, "CPH-SERVER");
        var request = await AccessRequestEndpointTests.CreatedAsync(created);
        (await ApprovalEndpointTests.DecideAsync(mikkel, request.Steps[0].StepId, "approve")).Dispose();
        (await ApprovalEndpointTests.DecideAsync(sofie, request.Steps[1].StepId, "approve")).Dispose();
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var eid = new EntitlementId(request.EntitlementId);
        return (await db.Fulfillments.AsNoTracking().SingleAsync(f => f.EntitlementId == eid, ct)).Id.Value;
    }
}
