using System.Net.Http.Json;
using Gatehouse.Application.Fulfillment.Dtos;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Access;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Fulfillment;

public sealed class FulfillmentDetailEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatehouseApiFactory _factory = new(postgres);

    public ValueTask InitializeAsync() => _factory.InitializeAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Detail_includes_command_status()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var scope = await _factory.CreateTenantScopeAsync("nordlys"))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            foreach (var instance in await db.PacsInstances.ToListAsync(ct))
            {
                instance.SetFulfillmentMode(FulfillmentMode.Connector);
            }

            await db.SaveChangesAsync(ct);
        }

        using var anna = _factory.CreateClientFor(DemoUser.Anna);
        using var mikkel = _factory.CreateClientFor(DemoUser.Mikkel);
        using var sofie = _factory.CreateClientFor(DemoUser.Sofie);
        using var created = await AccessRequestEndpointTests.PostRequestAsync(_factory, anna, "nordlys", "CPH-SERVER");
        var request = await AccessRequestEndpointTests.CreatedAsync(created);
        (await ApprovalEndpointTests.DecideAsync(mikkel, request.Steps[0].StepId, "approve")).Dispose();
        (await ApprovalEndpointTests.DecideAsync(sofie, request.Steps[1].StepId, "approve")).Dispose();
        await _factory.DrainMessagesAsync("nordlys");
        Guid fid;
        await using (var scope = await _factory.CreateTenantScopeAsync("nordlys"))
        {
            var eid = new Gatehouse.Domain.Access.EntitlementId(request.EntitlementId);
            fid = (await scope.ServiceProvider.GetRequiredService<TenantDbContext>().Fulfillments.AsNoTracking().SingleAsync(f => f.EntitlementId == eid, ct)).Id.Value;
        }

        using var jonas = _factory.CreateClientFor(DemoUser.Jonas);
        var detail = await jonas.GetFromJsonAsync<FulfillmentDetailDto>(new Uri($"/api/v1/fulfillments/{fid}", UriKind.Relative), ct);
        Assert.True(detail!.ConnectorManaged);
        Assert.Equal("Queued", detail.CommandStatus);
        Assert.Equal(0, detail.CommandAttempts);
        Assert.NotNull(detail.NextAttemptAt);
    }
}
