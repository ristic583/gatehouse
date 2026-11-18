using System.Text.Json;
using Gatehouse.Application.Connectors;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Adapters;
using Gatehouse.Connector.Contracts.Commands;
using Gatehouse.Connector.Contracts.Events;
using Gatehouse.Connector.Contracts.Protocol;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.LiveBuilding;
using Gatehouse.Domain.Sites;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Connectors;

/// <summary>Security review 2026-11-15: GH-CONN-01, GH-CONN-02, GH-ACCESS-01, GH-ACCESS-02 (dispatch) and GH-LIVE-01 (gateway).</summary>
public sealed class PhysicalAccessInvariantTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Tenant = "nordlys";
    private const string Area = "CPH-SERVER";
    private static readonly string CloudCaps = JsonSerializer.Serialize(
        new PacsCapabilities("CloudPacs", "1.0.0", false, true, true, true, true, [Gatehouse.Connector.Contracts.Adapters.DoorCommandKind.MomentaryUnlock], 100), ConnectorJson.Options);

    private readonly GatehouseApiFactory _factory = new(postgres);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => _factory.InitializeAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Future_grant_without_native_validity_is_deferred_and_not_polled()
    {
        var instanceId = await ConnectorModeAsync(CloudCaps);
        var now = DateTimeOffset.UtcNow;
        var start = now.AddDays(2);
        await SeedAsync(start, now.AddDays(10), EntitlementStatus.Approved, FulfillmentKind.Grant);
        await _factory.DrainMessagesAsync(Tenant);

        var grant = Assert.Single(await CommandsAsync(), c => c.Type == ConnectorCommandTypes.GrantAccess);
        Assert.Equal(start, grant.NextAttemptAt, TimeSpan.FromSeconds(1));
        Assert.True(grant.IsDeferred(DateTimeOffset.UtcNow));

        var delivered = await PollAsync(instanceId);
        Assert.Contains(delivered, e => e.Type == ConnectorCommandTypes.UpsertCardholder);
        Assert.DoesNotContain(delivered, e => e.Type == ConnectorCommandTypes.GrantAccess);
    }

    [Fact]
    public async Task Revoke_with_future_remaining_window_without_native_validity_removes_now()
    {
        await ConnectorModeAsync(CloudCaps);
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(now.AddDays(5), now.AddDays(60), EntitlementStatus.Approved, null);
        await SeedAsync(now.AddDays(-10), now.AddDays(5), EntitlementStatus.Revoked, FulfillmentKind.Revoke);
        await _factory.DrainMessagesAsync(Tenant);

        var commands = await CommandsAsync();
        var revoke = Assert.Single(commands, c => c.Type == ConnectorCommandTypes.RevokeAccess);
        Assert.False(revoke.IsDeferred(DateTimeOffset.UtcNow));
        var regrant = Assert.Single(commands, c => c.Type == ConnectorCommandTypes.GrantAccess);
        Assert.Equal(now.AddDays(5), regrant.NextAttemptAt, TimeSpan.FromSeconds(1));
        Assert.NotEqual(revoke.FulfillmentId, regrant.FulfillmentId);
    }

    [Fact]
    public async Task Failed_deferred_regrant_opens_a_fulfillment_task()
    {
        var instanceId = await ConnectorModeAsync(CloudCaps);
        var now = DateTimeOffset.UtcNow;
        var (remainder, _) = await SeedAsync(now.AddDays(5), now.AddDays(60), EntitlementStatus.Approved, null);
        var (_, revokeId) = await SeedAsync(now.AddDays(-10), now.AddDays(5), EntitlementStatus.Revoked, FulfillmentKind.Revoke);
        await _factory.DrainMessagesAsync(Tenant);

        // The regrant is its own Grant fulfillment of the remaining entitlement; at its start the PACS refuses it.
        ConnectorCommand regrant;
        FulfillmentId regrantFulfillment;
        await using (var scope = await _factory.CreateTenantScopeAsync(Tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            regrantFulfillment = (await db.Fulfillments.AsNoTracking().SingleAsync(f => f.EntitlementId == remainder && f.Kind == FulfillmentKind.Grant, Ct)).Id;
            var commands = await db.ConnectorCommands.Where(c => c.FulfillmentId == regrantFulfillment).OrderBy(c => c.Sequence).ToListAsync(Ct);
            foreach (var command in commands)
            {
                command.Deliver(now);
            }

            regrant = commands.Single(c => c.Type == ConnectorCommandTypes.GrantAccess);
            await db.SaveChangesAsync(Ct);
        }

        await using (var scope = await _factory.CreateTenantScopeAsync(Tenant))
        {
            var connector = new ConnectorContext();
            connector.Set(new TenantId(Tenant), instanceId, ConnectorId.New());
            var handler = ActivatorUtilities.CreateInstance<AckCommandsHandler>(scope.ServiceProvider, connector);
            await handler.HandleAsync(
                new AckRequest([new CommandAcked(regrant.Id.Value, regrant.IdempotencyKey, AckStatus.Failed, null, null,
                    new AckError("pacs.level_unknown", "The access level does not exist.", false))]),
                Ct);
        }

        await _factory.DrainMessagesAsync(Tenant);

        await using var check = await _factory.CreateTenantScopeAsync(Tenant);
        var checkDb = check.ServiceProvider.GetRequiredService<TenantDbContext>();
        Assert.NotEqual(revokeId, regrantFulfillment);
        Assert.Equal(FulfillmentStatus.Failed, (await checkDb.Fulfillments.AsNoTracking().SingleAsync(f => f.Id == regrantFulfillment, Ct)).Status);
        var task = await checkDb.StaffTasks.AsNoTracking().SingleAsync(t => t.SubjectId == regrantFulfillment.Value, Ct);
        Assert.Equal(Gatehouse.Domain.Tasks.StaffTaskKind.FulfillmentFailed, task.Kind);
    }

    [Fact]
    public async Task Deferred_regrant_reapplies_the_remainders_own_grant()
    {
        var instanceId = await ConnectorModeAsync(CloudCaps);
        var now = DateTimeOffset.UtcNow;
        var (current, _) = await SeedAsync(now.AddDays(-10), now.AddDays(5), EntitlementStatus.Approved, FulfillmentKind.Grant);
        var (remainder, remainderGrant) = await SeedAsync(now.AddDays(5), now.AddDays(60), EntitlementStatus.Approved,
            FulfillmentKind.Grant);
        await _factory.DrainMessagesAsync(Tenant);

        await using (var scope = await _factory.CreateTenantScopeAsync(Tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            foreach (var grant in await db.Fulfillments.Where(f => f.Kind == FulfillmentKind.Grant
                && f.Status == FulfillmentStatus.Pending).ToListAsync(Ct))
            {
                grant.ConfirmByAgent("test-connector", null, now);
            }

            await db.SaveChangesAsync(Ct);
            await RevokeAsync(db, current, instanceId);
        }

        await _factory.DrainMessagesAsync(Tenant);

        await using var check = await _factory.CreateTenantScopeAsync(Tenant);
        var checkDb = check.ServiceProvider.GetRequiredService<TenantDbContext>();
        var regrant = await checkDb.Fulfillments.AsNoTracking().SingleAsync(f => f.EntitlementId == remainder
            && f.Kind == FulfillmentKind.Grant, Ct);
        Assert.Equal((remainderGrant, 2), (regrant.Id, regrant.Revision));
        var command = await checkDb.ConnectorCommands.AsNoTracking().SingleAsync(c => c.Id == regrant.CommandId, Ct);
        Assert.True(command.IsDeferred(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Delivered_grant_of_superseded_fulfillment_is_never_redelivered()
    {
        var instanceId = await ConnectorModeAsync(null);
        var now = DateTimeOffset.UtcNow;
        var (_, grantId) = await SeedAsync(now.AddDays(-1), now.AddDays(10), EntitlementStatus.Approved, FulfillmentKind.Grant);
        await _factory.DrainMessagesAsync(Tenant);

        // The grant is in flight, then the entitlement is revoked (fulfillment superseded), then the agent reports a retryable failure.
        await using (var scope = await _factory.CreateTenantScopeAsync(Tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var commands = await db.ConnectorCommands.OrderBy(c => c.Sequence).ToListAsync(Ct);
            var upsert = commands.Single(c => c.Type == ConnectorCommandTypes.UpsertCardholder);
            upsert.Deliver(now);
            upsert.ApplyAck(CommandAckStatus.Applied, null, null, null, false, now);
            var grant = commands.Single(c => c.Type == ConnectorCommandTypes.GrantAccess);
            grant.Deliver(now);
            Assert.False(grant.Supersede(now));
            var fulfillment = await db.Fulfillments.SingleAsync(f => f.Id == grantId, Ct);
            Assert.True(fulfillment.Supersede(now).IsSuccess);
            Assert.Equal(CommandOutcome.RetryScheduled, grant.ApplyAck(CommandAckStatus.Failed, null, "x.transient", "down", true, now.AddMinutes(-1)));
            await db.SaveChangesAsync(Ct);
        }

        var delivered = await PollAsync(instanceId);

        Assert.DoesNotContain(delivered, e => e.Type == ConnectorCommandTypes.GrantAccess);
        Assert.Equal(ConnectorCommandStatus.Superseded, Assert.Single(await CommandsAsync(), c => c.Type == ConnectorCommandTypes.GrantAccess).Status);
    }

    [Fact]
    public async Task Fenced_grant_ack_keeps_fulfillment_superseded()
    {
        var instanceId = await ConnectorModeAsync(null);
        var now = DateTimeOffset.UtcNow;
        var (_, grantId) = await SeedAsync(now.AddDays(-1), now.AddDays(10), EntitlementStatus.Approved, FulfillmentKind.Grant);
        await _factory.DrainMessagesAsync(Tenant);
        ConnectorCommand grant;
        await using (var scope = await _factory.CreateTenantScopeAsync(Tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var commands = await db.ConnectorCommands.OrderBy(c => c.Sequence).ToListAsync(Ct);
            var upsert = commands.Single(c => c.Type == ConnectorCommandTypes.UpsertCardholder);
            upsert.Deliver(now);
            upsert.ApplyAck(CommandAckStatus.Applied, null, null, null, false, now);
            grant = commands.Single(c => c.Type == ConnectorCommandTypes.GrantAccess);
            grant.Deliver(now);
            await db.SaveChangesAsync(Ct);
        }

        // The agent received a High priority revoke first and fenced the in-flight grant.
        await using (var scope = await _factory.CreateTenantScopeAsync(Tenant))
        {
            var connector = new ConnectorContext();
            connector.Set(new TenantId(Tenant), instanceId, ConnectorId.New());
            var handler = ActivatorUtilities.CreateInstance<AckCommandsHandler>(scope.ServiceProvider, connector);
            var result = await handler.HandleAsync(
                new AckRequest([new CommandAcked(grant.Id.Value, grant.IdempotencyKey, AckStatus.Failed, null, null,
                    new AckError(AckCommandsHandler.GrantFencedCode, "fenced", false))]),
                Ct);
            Assert.True(Assert.Single(result.Value.Results).Accepted);
        }

        await _factory.DrainMessagesAsync(Tenant);

        await using var check = await _factory.CreateTenantScopeAsync(Tenant);
        var checkDb = check.ServiceProvider.GetRequiredService<TenantDbContext>();
        Assert.Equal(FulfillmentStatus.Superseded, (await checkDb.Fulfillments.AsNoTracking().SingleAsync(f => f.Id == grantId, Ct)).Status);
        var fencedCommand = await checkDb.ConnectorCommands.AsNoTracking().SingleAsync(c => c.Id == grant.Id, Ct);
        Assert.Equal(ConnectorCommandStatus.Failed, fencedCommand.Status);
    }

    [Fact]
    public async Task Revoke_targets_granted_level_after_remap()
    {
        await ConnectorModeAsync(null);
        var now = DateTimeOffset.UtcNow;
        var (entitlementId, _) = await SeedAsync(now.AddDays(-1), now.AddDays(10), EntitlementStatus.Approved, FulfillmentKind.Grant);
        await _factory.DrainMessagesAsync(Tenant);
        var granted = Payload<GrantAccess>(Assert.Single(await CommandsAsync(), c => c.Type == ConnectorCommandTypes.GrantAccess)).VendorAccessLevelRef;

        await using (var scope = await _factory.CreateTenantScopeAsync(Tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var areaId = (await db.Areas.SingleAsync(a => a.Code == Area, Ct)).Id;
            var mapping = await db.AccessLevelMappings.FirstAsync(m => m.AreaId == areaId, Ct);
            Assert.True(mapping.Update("AL-REMAPPED", "Remapped").IsSuccess);
            await RevokeAsync(db, entitlementId, mapping.PacsInstanceId);
        }

        await _factory.DrainMessagesAsync(Tenant);

        var revoke = Assert.Single(await CommandsAsync(), c => c.Type == ConnectorCommandTypes.RevokeAccess);
        Assert.Equal(granted, Payload<RevokeAccess>(revoke).VendorAccessLevelRef);
    }

    [Fact]
    public async Task Revoke_targets_granted_level_after_mapping_deleted()
    {
        await ConnectorModeAsync(null);
        var now = DateTimeOffset.UtcNow;
        var (entitlementId, _) = await SeedAsync(now.AddDays(-1), now.AddDays(10), EntitlementStatus.Approved, FulfillmentKind.Grant);
        await _factory.DrainMessagesAsync(Tenant);
        var granted = Payload<GrantAccess>(Assert.Single(await CommandsAsync(), c => c.Type == ConnectorCommandTypes.GrantAccess)).VendorAccessLevelRef;

        await using (var scope = await _factory.CreateTenantScopeAsync(Tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var areaId = (await db.Areas.SingleAsync(a => a.Code == Area, Ct)).Id;
            var mapping = await db.AccessLevelMappings.FirstAsync(m => m.AreaId == areaId, Ct);
            db.AccessLevelMappings.Remove(mapping);
            await RevokeAsync(db, entitlementId, mapping.PacsInstanceId);
        }

        await _factory.DrainMessagesAsync(Tenant);

        var revoke = Assert.Single(await CommandsAsync(), c => c.Type == ConnectorCommandTypes.RevokeAccess);
        Assert.Equal(granted, Payload<RevokeAccess>(revoke).VendorAccessLevelRef);
    }

    [Fact]
    public async Task Grant_for_ended_window_is_superseded_not_dispatched()
    {
        await ConnectorModeAsync(null);
        var now = DateTimeOffset.UtcNow;
        var (_, grantId) = await SeedAsync(now.AddDays(-10), now.AddDays(-1), EntitlementStatus.Approved, FulfillmentKind.Grant);
        await _factory.DrainMessagesAsync(Tenant);

        Assert.DoesNotContain(await CommandsAsync(), c => c.Type == ConnectorCommandTypes.GrantAccess);
        await using var scope = await _factory.CreateTenantScopeAsync(Tenant);
        var fulfillment = await scope.ServiceProvider.GetRequiredService<TenantDbContext>().Fulfillments.AsNoTracking().SingleAsync(f => f.Id == grantId, Ct);
        Assert.Equal(FulfillmentStatus.Superseded, fulfillment.Status);
    }

    [Fact]
    public async Task Momentary_unlock_past_its_deadline_is_not_delivered()
    {
        var instanceId = await ConnectorModeAsync(null);
        var now = DateTimeOffset.UtcNow;
        await using (var scope = await _factory.CreateTenantScopeAsync(Tenant))
        {
            var sp = scope.ServiceProvider;
            var factory = new ConnectorCommandFactory(sp.GetRequiredService<IConnectorSequencer>(), TimeProvider.System);
            var late = await factory.CreateForDoorAsync(instanceId, DoorCommandId.New(), DoorId.New(), ConnectorCommandTypes.MomentaryUnlock,
                new MomentaryUnlock(["D-LATE"], 5, "Delivery at the dock", now.AddSeconds(-1)), Ct);
            var onTime = await factory.CreateForDoorAsync(instanceId, DoorCommandId.New(), DoorId.New(), ConnectorCommandTypes.MomentaryUnlock,
                new MomentaryUnlock(["D-NOW"], 5, "Delivery at the dock", now.AddMinutes(1)), Ct);
            var db = sp.GetRequiredService<TenantDbContext>();
            db.ConnectorCommands.AddRange(late, onTime);
            await db.SaveChangesAsync(Ct);
        }

        var delivered = await PollAsync(instanceId);

        var unlock = Assert.Single(delivered, e => e.Type == ConnectorCommandTypes.MomentaryUnlock);
        Assert.Contains("D-NOW", unlock.Payload.GetRawText(), StringComparison.Ordinal);
        var expired = Assert.Single(await CommandsAsync(), c => c.Status == ConnectorCommandStatus.Failed);
        Assert.Equal(ConnectorCommandDelivery.DoorCommandExpiredCode, expired.LastError);
    }

    private static T Payload<T>(ConnectorCommand command) => JsonSerializer.Deserialize<T>(command.PayloadJson, ConnectorJson.Options)!;

    private static async Task RevokeAsync(TenantDbContext db, EntitlementId entitlementId, PacsInstanceId instanceId)
    {
        var entitlement = await db.Entitlements.SingleAsync(e => e.Id == entitlementId, Ct);
        db.Entry(entitlement).Property(e => e.Status).CurrentValue = EntitlementStatus.Revoked;
        db.Add(Gatehouse.Domain.Fulfillment.Fulfillment.Create(entitlementId, instanceId, FulfillmentKind.Revoke, CommandPriority.Normal, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(Ct);
    }

    private async Task<(EntitlementId Entitlement, FulfillmentId? Fulfillment)> SeedAsync(
        DateTimeOffset start, DateTimeOffset end, EntitlementStatus status, FulfillmentKind? kind)
    {
        await using var scope = await _factory.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var anna = await db.Persons.SingleAsync(p => p.Email == DemoUser.Anna.Email, Ct);
        var area = await db.Areas.SingleAsync(a => a.Code == Area, Ct);
        var mapping = await db.AccessLevelMappings.AsNoTracking().FirstAsync(m => m.AreaId == area.Id, Ct);
        var entitlement = Entitlement.Create(anna.Id, area.Id, anna.Id, start, end, end, "Invariant test", [new StepPlan(ApproverKind.AreaOwner, null)], start);
        entitlement.ClearDomainEvents();
        db.Add(entitlement);
        db.Entry(entitlement).Property(e => e.Status).CurrentValue = status;
        FulfillmentId? fulfillmentId = null;
        if (kind is { } k)
        {
            var fulfillment = Gatehouse.Domain.Fulfillment.Fulfillment.Create(entitlement.Id, mapping.PacsInstanceId, k, CommandPriority.Normal, DateTimeOffset.UtcNow);
            db.Add(fulfillment);
            fulfillmentId = fulfillment.Id;
        }

        await db.SaveChangesAsync(Ct);
        return (entitlement.Id, fulfillmentId);
    }

    /// <summary>All instances in connector mode (optionally with reported capabilities); returns the instance mapped for <see cref="Area"/>.</summary>
    private async Task<PacsInstanceId> ConnectorModeAsync(string? capabilities)
    {
        await using var scope = await _factory.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        foreach (var instance in await db.PacsInstances.ToListAsync(Ct))
        {
            instance.SetFulfillmentMode(FulfillmentMode.Connector);
            if (capabilities is not null)
            {
                instance.RecordHeartbeat(DateTimeOffset.UtcNow, "1.0.0-test", capabilities);
            }
        }

        await db.SaveChangesAsync(Ct);
        var areaId = (await db.Areas.SingleAsync(a => a.Code == Area, Ct)).Id;
        return (await db.AccessLevelMappings.AsNoTracking().FirstAsync(m => m.AreaId == areaId, Ct)).PacsInstanceId;
    }

    private async Task<IReadOnlyList<Gatehouse.Connector.Contracts.Protocol.CommandEnvelope>> PollAsync(PacsInstanceId instanceId)
    {
        await using var scope = await _factory.CreateTenantScopeAsync(Tenant);
        var connector = new ConnectorContext();
        connector.Set(new TenantId(Tenant), instanceId, ConnectorId.New());
        var handler = ActivatorUtilities.CreateInstance<ConnectorCommandDelivery>(scope.ServiceProvider);
        return await handler.SelectAndDeliverAsync(connector, 50, DateTimeOffset.UtcNow, Ct);
    }

    private async Task<List<ConnectorCommand>> CommandsAsync()
    {
        await using var scope = await _factory.CreateTenantScopeAsync(Tenant);
        return await scope.ServiceProvider.GetRequiredService<TenantDbContext>().ConnectorCommands.AsNoTracking().OrderBy(c => c.Sequence).ToListAsync(Ct);
    }
}
