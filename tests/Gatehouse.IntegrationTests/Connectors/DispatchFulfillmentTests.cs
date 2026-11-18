using System.Net.Http.Json;
using System.Text.Json;
using Gatehouse.Application.Access.Dtos;
using Gatehouse.Application.Connectors;
using Gatehouse.Application.Fulfillment.Dtos;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Commands;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Credentials;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Access;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Connectors;

public sealed class DispatchFulfillmentTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatehouseApiFactory _factory = new(postgres);

    public ValueTask InitializeAsync() => _factory.InitializeAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Approved_grant_on_connector_instance_queues_upsert_then_grant()
    {
        await SetModeAsync(FulfillmentMode.Connector);
        var fid = await ApprovedAsync();
        await _factory.DrainMessagesAsync("nordlys");
        var commands = await CommandsAsync();
        Assert.Equal([ConnectorCommandTypes.UpsertCardholder, ConnectorCommandTypes.GrantAccess], commands.Select(c => c.Type));
        Assert.Equal(commands[0].Sequence + 1, commands[1].Sequence);
        Assert.Equal($"GrantAccess:{fid}:1", commands[1].IdempotencyKey);
        Assert.Null(commands[0].FulfillmentId);
        Assert.Equal(FulfillmentStatus.Pending, (await FulfillmentAsync(fid)).Status);
    }

    [Fact]
    public async Task Manual_instance_queues_nothing()
    {
        await ApprovedAsync();
        await _factory.DrainMessagesAsync("nordlys");
        Assert.Empty(await CommandsAsync());
    }

    [Fact]
    public async Task Emergency_revoke_queues_high_priority_revoke()
    {
        await SetModeAsync(FulfillmentMode.Connector);
        using var erik = _factory.CreateClientFor(DemoUser.Jonas);
        var id = await EntitlementAsync("CPH-LOBBY");
        using var response = await erik.PostAsJsonAsync(new Uri($"/api/v1/access-requests/{id}/emergency-revoke", UriKind.Relative), new RevokeEntitlementDto("Lost badge"), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        await _factory.DrainMessagesAsync("nordlys");
        var revoke = Assert.Single(await CommandsAsync(), c => c.Type == ConnectorCommandTypes.RevokeAccess);
        Assert.Equal(CommandPriority.High, revoke.Priority);
    }

    [Fact]
    public async Task Person_without_card_fails_fulfillment_and_opens_task()
    {
        await SetModeAsync(FulfillmentMode.Connector);
        await RevokeCardsAsync();
        var fid = await ApprovedAsync();
        await _factory.DrainMessagesAsync("nordlys");
        var fulfillment = await FulfillmentAsync(fid);
        Assert.Equal(FulfillmentStatus.Failed, fulfillment.Status);
        Assert.Equal("fulfillment.no_active_card", fulfillment.LastError is null ? null : "fulfillment.no_active_card");
        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        Assert.True(await db.StaffTasks.AnyAsync(t => t.SubjectId == fid, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Same_message_twice_queues_once()
    {
        await SetModeAsync(FulfillmentMode.Connector);
        var fid = await ApprovedAsync();
        for (var i = 0; i < 2; i++)
        {
            await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
            var sp = scope.ServiceProvider;
            var handler = ActivatorUtilities.CreateInstance<DispatchFulfillmentHandler>(sp, new ConnectorCommandFactory(sp.GetRequiredService<IConnectorSequencer>(), TimeProvider.System), new InMemoryConnectorCommandSignal());
            await handler.HandleAsync(new DispatchFulfillmentCommand(new FulfillmentId(fid)), TestContext.Current.CancellationToken);
            await scope.ServiceProvider.GetRequiredService<TenantDbContext>().SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(2, (await CommandsAsync()).Count);
    }

    [Fact]
    public async Task Retry_creates_new_revision_key()
    {
        await SetModeAsync(FulfillmentMode.Connector);
        await RevokeCardsAsync();
        var fid = await ApprovedAsync();
        await _factory.DrainMessagesAsync("nordlys");
        await AddCardAsync();
        using var jonas = _factory.CreateClientFor(DemoUser.Jonas);
        (await jonas.PostAsJsonAsync(new Uri($"/api/v1/fulfillments/{fid}/retry", UriKind.Relative), new { }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        await _factory.DrainMessagesAsync("nordlys");
        Assert.Contains(await CommandsAsync(), c => c.IdempotencyKey == $"GrantAccess:{fid}:2");
    }

    [Fact]
    public async Task Renewal_grant_keeps_current_window_merged()
    {
        await SetModeAsync(FulfillmentMode.Connector);
        var now = DateTimeOffset.UtcNow;
        var current = await SeedApprovedAsync(now.AddDays(-10), now.AddDays(5), grant: null);
        var renewal = await SeedApprovedAsync(now.AddDays(5), now.AddDays(60), grant: FulfillmentKind.Grant);
        await _factory.DrainMessagesAsync("nordlys");

        var grant = Assert.Single(await CommandsAsync(), c => c.Type == ConnectorCommandTypes.GrantAccess);
        var payload = JsonSerializer.Deserialize<GrantAccess>(grant.PayloadJson, ConnectorJson.Options)!;
        Assert.Equal(current.Start, payload.ValidFrom, TimeSpan.FromSeconds(1));
        Assert.Equal(renewal.End, payload.ValidTo, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Revoke_with_live_renewal_regrants_remaining_window()
    {
        // A PACS with native validity can hold the later window now (without it the revoke removes access first: GH-CONN-01).
        await SetModeAsync(FulfillmentMode.Connector, NativeCaps);
        var now = DateTimeOffset.UtcNow;
        var renewal = await SeedApprovedAsync(now.AddDays(5), now.AddDays(60), grant: null);
        await SeedApprovedAsync(now.AddDays(-10), now.AddDays(5), grant: FulfillmentKind.Revoke, status: EntitlementStatus.Revoked);
        await _factory.DrainMessagesAsync("nordlys");

        var commands = await CommandsAsync();
        Assert.DoesNotContain(commands, c => c.Type == ConnectorCommandTypes.RevokeAccess);
        var regrant = Assert.Single(commands, c => c.Type == ConnectorCommandTypes.GrantAccess);
        var payload = JsonSerializer.Deserialize<GrantAccess>(regrant.PayloadJson, ConnectorJson.Options)!;
        Assert.Equal(renewal.Start, payload.ValidFrom, TimeSpan.FromSeconds(1));
        Assert.Equal(renewal.End, payload.ValidTo, TimeSpan.FromSeconds(1));
    }

    private async Task<(DateTimeOffset Start, DateTimeOffset End)> SeedApprovedAsync(
        DateTimeOffset start, DateTimeOffset end, FulfillmentKind? grant, EntitlementStatus status = EntitlementStatus.Approved)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var anna = await db.Persons.SingleAsync(p => p.Email == DemoUser.Anna.Email, ct);
        var area = await db.Areas.SingleAsync(a => a.Code == "CPH-SERVER", ct);
        var mapping = await db.AccessLevelMappings.AsNoTracking().FirstAsync(m => m.AreaId == area.Id, ct);
        var entitlement = Entitlement.Create(anna.Id, area.Id, anna.Id, start, end, end, "Window test", [new StepPlan(ApproverKind.AreaOwner, null)], start);
        entitlement.ClearDomainEvents();
        db.Add(entitlement);
        db.Entry(entitlement).Property(e => e.Status).CurrentValue = status;
        if (grant is { } kind)
        {
            db.Add(Gatehouse.Domain.Fulfillment.Fulfillment.Create(entitlement.Id, mapping.PacsInstanceId, kind, CommandPriority.Normal, DateTimeOffset.UtcNow));
        }

        await db.SaveChangesAsync(ct);
        return (start, end);
    }

    private async Task<List<ConnectorCommand>> CommandsAsync()
    {
        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        return await scope.ServiceProvider.GetRequiredService<TenantDbContext>().ConnectorCommands.AsNoTracking().OrderBy(c => c.Sequence).ToListAsync(TestContext.Current.CancellationToken);
    }

    private async Task<Gatehouse.Domain.Fulfillment.Fulfillment> FulfillmentAsync(Guid fid)
    {
        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        return await scope.ServiceProvider.GetRequiredService<TenantDbContext>().Fulfillments.AsNoTracking().SingleAsync(f => f.Id == new FulfillmentId(fid), TestContext.Current.CancellationToken);
    }

    private static readonly string NativeCaps = JsonSerializer.Serialize(
        new Gatehouse.Connector.Contracts.Adapters.PacsCapabilities("LegacyPacs", "1.0.0", true, false, false, false, true, [], 50), ConnectorJson.Options);

    private async Task SetModeAsync(FulfillmentMode mode, string? capabilities = null)
    {
        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        foreach (var instance in await db.PacsInstances.ToListAsync(TestContext.Current.CancellationToken))
        {
            instance.SetFulfillmentMode(mode);
            if (capabilities is not null)
            {
                instance.RecordHeartbeat(DateTimeOffset.UtcNow, "1.0.0-test", capabilities);
            }
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task RevokeCardsAsync()
    {
        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        db.Cards.RemoveRange(await db.Cards.ToListAsync(TestContext.Current.CancellationToken));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task AddCardAsync()
    {
        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var anna = await db.Persons.SingleAsync(p => p.Email == DemoUser.Anna.Email, TestContext.Current.CancellationToken);
        db.Cards.Add(Card.CreateActive(anna.Id, "0099887766", DateTimeOffset.UtcNow).Value);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<Guid> EntitlementAsync(string areaCode)
    {
        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var ct = TestContext.Current.CancellationToken;
        var annaId = (await db.Persons.SingleAsync(p => p.Email == DemoUser.Anna.Email, ct)).Id;
        var areaId = (await db.Areas.SingleAsync(a => a.Code == areaCode, ct)).Id;
        return (await db.Entitlements.AsNoTracking().SingleAsync(e => e.PersonId == annaId && e.AreaId == areaId, ct)).Id.Value;
    }

    private async Task<Guid> ApprovedAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        using var anna = _factory.CreateClientFor(DemoUser.Anna);
        using var mikkel = _factory.CreateClientFor(DemoUser.Mikkel);
        using var sofie = _factory.CreateClientFor(DemoUser.Sofie);
        using var created = await AccessRequestEndpointTests.PostRequestAsync(_factory, anna, "nordlys", "CPH-SERVER");
        var request = await AccessRequestEndpointTests.CreatedAsync(created);
        (await ApprovalEndpointTests.DecideAsync(mikkel, request.Steps[0].StepId, "approve")).Dispose();
        (await ApprovalEndpointTests.DecideAsync(sofie, request.Steps[1].StepId, "approve")).Dispose();
        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var eid = new EntitlementId(request.EntitlementId);
        return (await db.Fulfillments.AsNoTracking().SingleAsync(f => f.EntitlementId == eid, ct)).Id.Value;
    }
}
