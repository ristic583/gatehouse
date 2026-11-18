using System.Net;
using System.Net.Http.Json;
using Gatehouse.Application.Fulfillment.Dtos;
using Gatehouse.Domain.People;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Access;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Fulfillment;

public sealed class FulfillmentQueueEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatehouseApiFactory _factory = new(postgres);

    public ValueTask InitializeAsync() => _factory.InitializeAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    public static async Task<List<FulfillmentQueueItemDto>> ListAsync(HttpClient client, string view) =>
        (await client.GetFromJsonAsync<List<FulfillmentQueueItemDto>>(new Uri("/api/v1/fulfillments?view=" + view, UriKind.Relative), TestContext.Current.CancellationToken))!;

    [Fact]
    public async Task Approved_request_appears_in_open_queue()
    {
        using var anna = _factory.CreateClientFor(DemoUser.Anna);
        using var mikkel = _factory.CreateClientFor(DemoUser.Mikkel);
        using var sofie = _factory.CreateClientFor(DemoUser.Sofie);
        using var jonas = _factory.CreateClientFor(DemoUser.Jonas);
        using var created = await AccessRequestEndpointTests.PostRequestAsync(_factory, anna, "nordlys", "CPH-SERVER");
        var request = await AccessRequestEndpointTests.CreatedAsync(created);
        (await ApprovalEndpointTests.DecideAsync(mikkel, request.Steps[0].StepId, "approve")).Dispose();
        (await ApprovalEndpointTests.DecideAsync(sofie, request.Steps[1].StepId, "approve")).Dispose();

        var item = Assert.Single((await ListAsync(jonas, "open")), i => i.EntitlementId == request.EntitlementId);
        Assert.Equal("Desired", item.Status);
        Assert.Equal("Grant", item.Kind);
        Assert.Equal("HQ-Copenhagen", item.PacsInstanceName);
        Assert.Equal("AL-CPH-SERVER", item.VendorAccessLevelRef);
    }

    [Fact]
    public async Task Seeded_confirmations_appear_in_done_view()
    {
        using var jonas = _factory.CreateClientFor(DemoUser.Jonas);
        var done = await ListAsync(jonas, "done");
        Assert.NotEmpty(done);
        Assert.All(done, i => Assert.True(i.Status is "Confirmed" or "Superseded"));
    }

    [Fact]
    public async Task Drift_appears_in_open_view()
    {
        var ct = TestContext.Current.CancellationToken;
        using var jonas = _factory.CreateClientFor(DemoUser.Jonas);
        var confirmed = (await ListAsync(jonas, "done")).First(i => i.Status == "Confirmed");
        await using (var scope = await _factory.CreateTenantScopeAsync("nordlys"))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var id = new Gatehouse.Domain.Fulfillment.FulfillmentId(confirmed.FulfillmentId);
            await db.Fulfillments.Where(f => f.Id == id)
                .ExecuteUpdateAsync(u => u.SetProperty(f => f.Status, Gatehouse.Domain.Fulfillment.FulfillmentStatus.Drift), ct);
        }

        var item = Assert.Single(await ListAsync(jonas, "open"), i => i.FulfillmentId == confirmed.FulfillmentId);
        Assert.Equal("Drift", item.Status);
    }

    [Fact]
    public async Task Detail_shows_desired_and_confirmed_state()
    {
        var ct = TestContext.Current.CancellationToken;
        using var jonas = _factory.CreateClientFor(DemoUser.Jonas);
        var item = (await ListAsync(jonas, "done")).First(i => i.Subject.DisplayName.StartsWith("Anna", StringComparison.Ordinal) && i.Area.Name == "Lobby");
        var detail = await jonas.GetFromJsonAsync<FulfillmentDetailDto>(new Uri($"/api/v1/fulfillments/{item.FulfillmentId}", UriKind.Relative), ct);

        Assert.Equal("Grant", detail!.Desired.Action);
        Assert.NotNull(detail.Confirmed);
        Assert.Equal("Staff", detail.Confirmed.ConfirmedByKind);
        Assert.StartsWith("Jonas", detail.Confirmed.ConfirmedByName, StringComparison.Ordinal);
        Assert.True(detail.Desired.ValidFrom < detail.Desired.ValidTo);
    }

    [Fact]
    public async Task Employee_and_tenant_admin_get_403()
    {
        foreach (var user in new[] { DemoUser.Anna, DemoUser.Freja })
        {
            using var client = _factory.CreateClientFor(user);
            using var response = await client.GetAsync(new Uri("/api/v1/fulfillments", UriKind.Relative), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task Site_scoped_staff_sees_only_their_sites()
    {
        var ct = TestContext.Current.CancellationToken;
        Guid assignmentId;
        await using (var scope = await _factory.CreateTenantScopeAsync("nordlys"))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var person = await db.Persons.SingleAsync(p => p.Email == "person-01@nordlys.test", ct);
            var site = await db.Sites.SingleAsync(s => s.Code == "AAR-WH", ct);
            var assignment = RoleAssignment.Create(person.Id, AssignedRole.SecurityStaff, site.Id).Value;
            db.RoleAssignments.Add(assignment);
            await db.SaveChangesAsync(ct);
            assignmentId = assignment.Id.Value;
        }

        try
        {
            using var jonas = _factory.CreateClientFor(DemoUser.Jonas);
            var cphItem = (await ListAsync(jonas, "done")).First();
            using var scoped = _factory.CreateClientFor("test-person-01", "person-01@nordlys.test", "nordlys");
            Assert.DoesNotContain(await ListAsync(scoped, "done"), i => i.Area.SiteName == "HQ Copenhagen");
            Assert.DoesNotContain(await ListAsync(scoped, "open"), i => i.Area.SiteName == "HQ Copenhagen");
            using var response = await scoped.GetAsync(new Uri($"/api/v1/fulfillments/{cphItem.FulfillmentId}", UriKind.Relative), ct);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var id = new RoleAssignmentId(assignmentId);
            db.RoleAssignments.Remove(await db.RoleAssignments.SingleAsync(r => r.Id == id, ct));
            await db.SaveChangesAsync(ct);
        }
    }
}
