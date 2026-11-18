using System.Net.Http.Json;
using Gatehouse.Application.Fulfillment.Dtos;
using Gatehouse.Domain.Tasks;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Access;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Fulfillment;

public sealed class FulfillmentTaskConsumerTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatehouseApiFactory _factory = new(postgres);

    public ValueTask InitializeAsync() => _factory.InitializeAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Failed_fulfillment_opens_task_after_drain()
    {
        var ct = TestContext.Current.CancellationToken;
        var fid = await FailedAsync();
        await _factory.DrainMessagesAsync("nordlys");

        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var task = await scope.ServiceProvider.GetRequiredService<TenantDbContext>().StaffTasks.AsNoTracking()
            .SingleAsync(t => t.SubjectId == fid && t.Kind == StaffTaskKind.FulfillmentFailed, ct);
        Assert.Equal(StaffTaskStatus.Open, task.Status);
    }

    [Fact]
    public async Task Confirmed_fulfillment_resolves_task_after_drain()
    {
        var ct = TestContext.Current.CancellationToken;
        var fid = await FailedAsync();
        await _factory.DrainMessagesAsync("nordlys");
        using var jonas = _factory.CreateClientFor(DemoUser.Jonas);
        (await jonas.PostAsJsonAsync(new Uri($"/api/v1/fulfillments/{fid}/retry", UriKind.Relative), new { }, ct)).Dispose();
        (await jonas.PostAsJsonAsync(new Uri($"/api/v1/fulfillments/{fid}/confirm", UriKind.Relative), new { }, ct)).Dispose();
        await _factory.DrainMessagesAsync("nordlys");

        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var task = await scope.ServiceProvider.GetRequiredService<TenantDbContext>().StaffTasks.AsNoTracking().SingleAsync(t => t.SubjectId == fid, ct);
        Assert.Equal(StaffTaskStatus.Resolved, task.Status);
    }

    private async Task<Guid> FailedAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        using var anna = _factory.CreateClientFor(DemoUser.Anna);
        using var mikkel = _factory.CreateClientFor(DemoUser.Mikkel);
        using var sofie = _factory.CreateClientFor(DemoUser.Sofie);
        using var jonas = _factory.CreateClientFor(DemoUser.Jonas);
        using var created = await AccessRequestEndpointTests.PostRequestAsync(_factory, anna, "nordlys", "CPH-SERVER");
        var request = await AccessRequestEndpointTests.CreatedAsync(created);
        (await ApprovalEndpointTests.DecideAsync(mikkel, request.Steps[0].StepId, "approve")).Dispose();
        (await ApprovalEndpointTests.DecideAsync(sofie, request.Steps[1].StepId, "approve")).Dispose();
        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var eid = new Gatehouse.Domain.Access.EntitlementId(request.EntitlementId);
        var fid = (await db.Fulfillments.AsNoTracking().SingleAsync(f => f.EntitlementId == eid, ct)).Id.Value;
        (await jonas.PostAsJsonAsync(new Uri($"/api/v1/fulfillments/{fid}/fail", UriKind.Relative), new FailFulfillmentDto("printer offline"), ct)).Dispose();
        return fid;
    }
}
