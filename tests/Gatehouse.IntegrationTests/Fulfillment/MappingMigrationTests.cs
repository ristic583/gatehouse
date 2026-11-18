using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gatehouse.Application.Fulfillment.Dtos;
using Gatehouse.Application.Fulfillment.PacsAdmin;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Commands;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FulfillmentEntity = Gatehouse.Domain.Fulfillment.Fulfillment;

namespace Gatehouse.IntegrationTests.Fulfillment;

/// <summary>M11-16 (GH-ACCESS-01): a mapping edit revokes the granted level first; the new level is granted only after that revoke is
/// out.</summary>
public sealed class MappingMigrationTests(PostgresFixture postgres) : IAsyncLifetime
{
    internal const string Tenant = "nordlys";
    internal const string Area = "CPH-SERVER";
    internal const string NewLevel = "AL-MOVED";

    private readonly GatehouseApiFactory _factory = new(postgres);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => _factory.InitializeAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Follow_up_grant_never_precedes_the_revoke()
    {
        await ConnectorModeAsync(_factory);
        var (entitlementId, oldLevel) = await ConfirmedConnectorGrantAsync(_factory);

        await EditMappingAsync(_factory, NewLevel);

        // Before the revoke is dispatched the grant is untouched and nothing carries the new level.
        AssertGrant(await FulfillmentsAsync(_factory, entitlementId), FulfillmentStatus.Confirmed, 1);
        Assert.DoesNotContain(await CommandsAsync(_factory), c => c.Type == ConnectorCommandTypes.GrantAccess
            && Payload<GrantAccess>(c).VendorAccessLevelRef == NewLevel);

        await _factory.DrainMessagesAsync(Tenant);

        var fulfillments = await FulfillmentsAsync(_factory, entitlementId);
        var ids = fulfillments.Select(f => (FulfillmentId?)f.Id).ToList();
        var commands = (await CommandsAsync(_factory)).Where(c => ids.Contains(c.FulfillmentId)).ToList();
        var revoke = Assert.Single(commands, c => c.Type == ConnectorCommandTypes.RevokeAccess);
        var grant = Assert.Single(commands, c => c.Type == ConnectorCommandTypes.GrantAccess
            && Payload<GrantAccess>(c).VendorAccessLevelRef == NewLevel);
        AssertGrant(fulfillments, FulfillmentStatus.Pending, 2);
        Assert.Equal(oldLevel, Payload<RevokeAccess>(revoke).VendorAccessLevelRef);
        Assert.True(revoke.Sequence < grant.Sequence);
        Assert.NotEqual(ConnectorCommandStatus.Superseded, revoke.Status);
        Assert.NotEqual(ConnectorCommandStatus.Superseded, grant.Status);
    }

    [Fact]
    public async Task Entitlement_ended_before_the_revoke_is_out_gets_no_new_level()
    {
        await ConnectorModeAsync(_factory);
        var (entitlementId, _) = await ConfirmedConnectorGrantAsync(_factory);
        await EditMappingAsync(_factory, NewLevel);

        await using (var scope = await _factory.CreateTenantScopeAsync(Tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var entitlement = await db.Entitlements.SingleAsync(e => e.Id == entitlementId, Ct);
            db.Entry(entitlement).Property(e => e.Status).CurrentValue = EntitlementStatus.Revoked;
            await db.SaveChangesAsync(Ct);
        }

        await _factory.DrainMessagesAsync(Tenant);

        AssertGrant(await FulfillmentsAsync(_factory, entitlementId), FulfillmentStatus.Confirmed, 1);
        Assert.DoesNotContain(await CommandsAsync(_factory), c => c.Type == ConnectorCommandTypes.GrantAccess
            && Payload<GrantAccess>(c).VendorAccessLevelRef == NewLevel);
    }

    [Fact]
    public async Task Revoke_after_migration_reuses_the_revoke_and_removes_the_new_level()
    {
        await ConnectorModeAsync(_factory);
        var (entitlementId, _) = await ConfirmedConnectorGrantAsync(_factory);
        await EditMappingAsync(_factory, NewLevel);
        await _factory.DrainMessagesAsync(Tenant);

        await using (var scope = await _factory.CreateTenantScopeAsync(Tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            foreach (var settled in await db.Fulfillments.Where(f => f.EntitlementId == entitlementId).ToListAsync(Ct))
            {
                Assert.True(settled.ConfirmByAgent("test-connector", null, DateTimeOffset.UtcNow).IsSuccess);
            }

            var entitlement = await db.Entitlements.SingleAsync(e => e.Id == entitlementId, Ct);
            Assert.True(entitlement.Revoke(entitlement.PersonId, "left", Gatehouse.Domain.Access.RevocationKind.Normal,
                DateTimeOffset.UtcNow).IsSuccess);
            var planner = scope.ServiceProvider.GetRequiredService<Gatehouse.Application.Fulfillment.FulfillmentPlanner>();
            await planner.PlanRevocationAsync(entitlement, CommandPriority.Normal,
                Gatehouse.Domain.Audit.AuditActor.ForSystem("test"), DateTimeOffset.UtcNow, Ct);
            await db.SaveChangesAsync(Ct);
        }

        await _factory.DrainMessagesAsync(Tenant);

        var fulfillments = await FulfillmentsAsync(_factory, entitlementId);
        var revoke = Assert.Single(fulfillments, f => f.Kind == FulfillmentKind.Revoke);
        Assert.Equal((FulfillmentStatus.Pending, 2), (revoke.Status, revoke.Revision));
        var last = (await CommandsAsync(_factory)).Last(c => c.FulfillmentId == revoke.Id && c.Type == ConnectorCommandTypes.RevokeAccess);
        Assert.Equal(NewLevel, Payload<RevokeAccess>(last).VendorAccessLevelRef);
    }

    [Fact]
    public async Task Manual_instance_grants_new_level_only_after_staff_confirm_the_revoke()
    {
        using var jonas = _factory.CreateClientFor(DemoUser.Jonas);
        var (entitlementId, grantId) = await SeedGrantAsync(_factory);
        (await PostAsync(jonas, grantId, "confirm")).Dispose();
        var oldLevel = await MappedLevelAsync(_factory);

        await EditMappingAsync(_factory, NewLevel);

        var revoke = Assert.Single(await FulfillmentsAsync(_factory, entitlementId), f => f.Kind == FulfillmentKind.Revoke);
        Assert.Equal(FulfillmentStatus.Desired, revoke.Status);
        AssertGrant(await FulfillmentsAsync(_factory, entitlementId), FulfillmentStatus.Confirmed, 1);
        var detail = (await jonas.GetFromJsonAsync<FulfillmentDetailDto>(new Uri($"/api/v1/fulfillments/{revoke.Id.Value}",
            UriKind.Relative), Ct))!;
        Assert.Equal(oldLevel, detail.Desired.VendorAccessLevelRef);

        using var confirmed = await PostAsync(jonas, revoke.Id.Value, "confirm");
        Assert.True(confirmed.IsSuccessStatusCode, await confirmed.Content.ReadAsStringAsync(Ct));

        AssertGrant(await FulfillmentsAsync(_factory, entitlementId), FulfillmentStatus.Desired, 2);
        var grantDetail = (await jonas.GetFromJsonAsync<FulfillmentDetailDto>(new Uri($"/api/v1/fulfillments/{grantId}",
            UriKind.Relative), Ct))!;
        Assert.Equal(NewLevel, grantDetail.Desired.VendorAccessLevelRef);
    }

    internal static void AssertGrant(List<FulfillmentEntity> fulfillments, FulfillmentStatus status, int revision)
    {
        var grant = Assert.Single(fulfillments, f => f.Kind == FulfillmentKind.Grant);
        Assert.Equal((status, revision), (grant.Status, grant.Revision));
    }

    internal static T Payload<T>(ConnectorCommand command) => JsonSerializer.Deserialize<T>(command.PayloadJson, ConnectorJson.Options)!;

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, Guid id, string action) =>
        client.PostAsJsonAsync(new Uri($"/api/v1/fulfillments/{id}/{action}", UriKind.Relative), new { }, Ct);

    /// <summary>Anna's approved, running entitlement on <see cref="Area"/> with a Desired grant fulfillment on the mapped
    /// instance.</summary>
    internal static async Task<(EntitlementId Entitlement, Guid Grant)> SeedGrantAsync(GatehouseApiFactory factory)
    {
        await using var scope = await factory.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var anna = await db.Persons.SingleAsync(p => p.Email == DemoUser.Anna.Email, Ct);
        var area = await db.Areas.SingleAsync(a => a.Code == Area, Ct);
        var mapping = await db.AccessLevelMappings.AsNoTracking().FirstAsync(m => m.AreaId == area.Id, Ct);
        var now = DateTimeOffset.UtcNow;
        var (start, end) = (now.AddDays(-1), now.AddDays(10));
        var entitlement = Entitlement.Create(anna.Id, area.Id, anna.Id, start, end, end, "Migration test",
            [new StepPlan(ApproverKind.AreaOwner, null)], start);
        entitlement.ClearDomainEvents();
        db.Add(entitlement);
        db.Entry(entitlement).Property(e => e.Status).CurrentValue = EntitlementStatus.Approved;
        var grant = FulfillmentEntity.Create(entitlement.Id, mapping.PacsInstanceId, FulfillmentKind.Grant, CommandPriority.Normal, now);
        db.Add(grant);
        await db.SaveChangesAsync(Ct);
        return (entitlement.Id, grant.Id.Value);
    }

    /// <summary>Seeds the grant, dispatches it and confirms it as the agent would; returns the granted level.</summary>
    internal static async Task<(EntitlementId Entitlement, string Level)> ConfirmedConnectorGrantAsync(GatehouseApiFactory factory)
    {
        var (entitlementId, grantId) = await SeedGrantAsync(factory);
        await factory.DrainMessagesAsync(Tenant);
        await using var scope = await factory.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var grant = await db.Fulfillments.SingleAsync(f => f.Id == new FulfillmentId(grantId), Ct);
        Assert.True(grant.ConfirmByAgent("test-connector", null, DateTimeOffset.UtcNow).IsSuccess);
        await db.SaveChangesAsync(Ct);
        var command = await db.ConnectorCommands.AsNoTracking().SingleAsync(c => c.Id == grant.CommandId, Ct);
        return (entitlementId, Payload<GrantAccess>(command).VendorAccessLevelRef);
    }

    internal static async Task ConnectorModeAsync(GatehouseApiFactory factory)
    {
        await using var scope = await factory.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        foreach (var instance in await db.PacsInstances.ToListAsync(Ct))
        {
            instance.SetFulfillmentMode(FulfillmentMode.Connector);
        }

        await db.SaveChangesAsync(Ct);
    }

    /// <summary>PUTs the instance's mappings as the tenant admin with <see cref="Area"/> set to <paramref name="level"/> (null =
    /// removed).</summary>
    internal static async Task EditMappingAsync(GatehouseApiFactory factory, string? level)
    {
        List<AccessLevelMappingRequest> request;
        Guid instanceId;
        await using (var scope = await factory.CreateTenantScopeAsync(Tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var areaId = (await db.Areas.SingleAsync(a => a.Code == Area, Ct)).Id;
            var target = await db.AccessLevelMappings.AsNoTracking().FirstAsync(m => m.AreaId == areaId, Ct);
            instanceId = target.PacsInstanceId.Value;
            var all = await db.AccessLevelMappings.AsNoTracking().Where(m => m.PacsInstanceId == target.PacsInstanceId).ToListAsync(Ct);
            request = all.Where(m => m.AreaId != areaId || level is not null)
                .Select(m => m.AreaId == areaId ? new AccessLevelMappingRequest(m.AreaId.Value, level!, level)
                    : new AccessLevelMappingRequest(m.AreaId.Value, m.VendorAccessLevelRef, m.DisplayName))
                .ToList();
        }

        using var freja = factory.CreateClientFor(DemoUser.Freja);
        using var response = await freja.PutAsJsonAsync(new Uri($"/api/v1/admin/pacs-instances/{instanceId}/mappings",
            UriKind.Relative), request, ApiJson.Options, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    internal static async Task<string> MappedLevelAsync(GatehouseApiFactory factory)
    {
        await using var scope = await factory.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var areaId = (await db.Areas.SingleAsync(a => a.Code == Area, Ct)).Id;
        return (await db.AccessLevelMappings.AsNoTracking().FirstAsync(m => m.AreaId == areaId, Ct)).VendorAccessLevelRef;
    }

    internal static async Task<List<FulfillmentEntity>> FulfillmentsAsync(GatehouseApiFactory factory, EntitlementId entitlementId)
    {
        await using var scope = await factory.CreateTenantScopeAsync(Tenant);
        return await scope.ServiceProvider.GetRequiredService<TenantDbContext>()
            .Fulfillments.AsNoTracking().Where(f => f.EntitlementId == entitlementId).ToListAsync(Ct);
    }

    internal static async Task<List<ConnectorCommand>> CommandsAsync(GatehouseApiFactory factory)
    {
        await using var scope = await factory.CreateTenantScopeAsync(Tenant);
        return await scope.ServiceProvider.GetRequiredService<TenantDbContext>()
            .ConnectorCommands.AsNoTracking().OrderBy(c => c.Sequence).ToListAsync(Ct);
    }

    internal static async Task<int> AuditCountAsync(GatehouseApiFactory factory, string action)
    {
        await using var scope = await factory.CreateTenantScopeAsync(Tenant);
        return await scope.ServiceProvider.GetRequiredService<TenantDbContext>()
            .AuditEntries.AsNoTracking().CountAsync(a => a.Action == action, Ct);
    }
}
