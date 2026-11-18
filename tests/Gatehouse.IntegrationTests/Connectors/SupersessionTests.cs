using System.Net.Http.Json;
using Gatehouse.Application.Access.Dtos;
using Gatehouse.Application.Connectors;
using Gatehouse.Connector.Contracts;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Access;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Connectors;

public sealed class SupersessionTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatehouseApiFactory _factory = new(postgres);

    public ValueTask InitializeAsync() => _factory.InitializeAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Revoke_supersedes_queued_grant_of_same_entitlement()
    {
        await SetConnectorModeAsync();
        var (entitlementId, _) = await ApprovedAsync();
        await _factory.DrainMessagesAsync("nordlys");
        await RevokeAsync(entitlementId);
        await _factory.DrainMessagesAsync("nordlys");
        var commands = await CommandsAsync();
        Assert.Equal(ConnectorCommandStatus.Superseded, Assert.Single(commands, c => c.Type == ConnectorCommandTypes.GrantAccess).Status);
        Assert.Equal(ConnectorCommandStatus.Queued, Assert.Single(commands, c => c.Type == ConnectorCommandTypes.RevokeAccess).Status);
    }

    [Fact]
    public async Task Revoke_leaves_other_entitlements_commands()
    {
        await SetConnectorModeAsync();
        await ApprovedAsync();
        await _factory.DrainMessagesAsync("nordlys");
        var lobby = await EntitlementAsync("CPH-LOBBY");
        await RevokeAsync(lobby);
        await _factory.DrainMessagesAsync("nordlys");
        var commands = await CommandsAsync();
        Assert.Equal(ConnectorCommandStatus.Queued, Assert.Single(commands, c => c.Type == ConnectorCommandTypes.GrantAccess).Status);
    }

    [Fact]
    public async Task Superseded_event_supersedes_queued_commands_once()
    {
        await SetConnectorModeAsync();
        var (_, fid) = await ApprovedAsync();
        await _factory.DrainMessagesAsync("nordlys");
        for (var i = 0; i < 2; i++)
        {
            await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
            var handler = ActivatorUtilities.CreateInstance<SupersedeFulfillmentCommandsHandler>(scope.ServiceProvider);
            Assert.True((await handler.HandleAsync(new SupersedeFulfillmentCommandsCommand(new FulfillmentId(fid)), TestContext.Current.CancellationToken)).IsSuccess);
            await scope.ServiceProvider.GetRequiredService<TenantDbContext>().SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var commands = await CommandsAsync();
        Assert.Equal(ConnectorCommandStatus.Superseded, Assert.Single(commands, c => c.Type == ConnectorCommandTypes.GrantAccess).Status);
        Assert.Equal(ConnectorCommandStatus.Queued, Assert.Single(commands, c => c.Type == ConnectorCommandTypes.UpsertCardholder).Status);
    }

    private async Task RevokeAsync(Guid entitlementId)
    {
        using var jonas = _factory.CreateClientFor(DemoUser.Jonas);
        using var response = await jonas.PostAsJsonAsync(new Uri($"/api/v1/access-requests/{entitlementId}/emergency-revoke", UriKind.Relative), new RevokeEntitlementDto("Lost badge"), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<List<ConnectorCommand>> CommandsAsync()
    {
        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        return await scope.ServiceProvider.GetRequiredService<TenantDbContext>().ConnectorCommands.AsNoTracking().OrderBy(c => c.Sequence).ToListAsync(TestContext.Current.CancellationToken);
    }

    private async Task SetConnectorModeAsync()
    {
        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        foreach (var instance in await db.PacsInstances.ToListAsync(TestContext.Current.CancellationToken))
        {
            instance.SetFulfillmentMode(FulfillmentMode.Connector);
        }

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

    private async Task<(Guid EntitlementId, Guid FulfillmentId)> ApprovedAsync()
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
        var eid = new Gatehouse.Domain.Access.EntitlementId(request.EntitlementId);
        return (request.EntitlementId, (await db.Fulfillments.AsNoTracking().SingleAsync(f => f.EntitlementId == eid, ct)).Id.Value);
    }
}
