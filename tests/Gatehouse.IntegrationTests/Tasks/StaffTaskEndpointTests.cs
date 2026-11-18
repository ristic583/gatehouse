using System.Net;
using System.Net.Http.Json;
using Gatehouse.Application.Fulfillment.Dtos;
using Gatehouse.Application.Tasks.Dtos;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Access;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Tasks;

public sealed class StaffTaskEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatehouseApiFactory _factory = new(postgres);

    public ValueTask InitializeAsync() => _factory.InitializeAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string url, object? body = null) =>
        client.PostAsJsonAsync(new Uri(url, UriKind.Relative), body ?? new { }, TestContext.Current.CancellationToken);

    private static async Task<List<StaffTaskDto>> ListAsync(HttpClient client, string status = "open") =>
        (await client.GetFromJsonAsync<List<StaffTaskDto>>(new Uri("/api/v1/tasks?status=" + status, UriKind.Relative), TestContext.Current.CancellationToken))!;

    private async Task<StaffTaskDto> FailedTaskAsync()
    {
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
        var fid = (await db.Fulfillments.AsNoTracking().SingleAsync(f => f.EntitlementId == eid, TestContext.Current.CancellationToken)).Id.Value;
        (await PostAsync(jonas, $"/api/v1/fulfillments/{fid}/fail", new FailFulfillmentDto("printer offline"))).Dispose();
        await _factory.DrainMessagesAsync("nordlys");
        return (await ListAsync(jonas)).Single(t => t.SubjectId == fid);
    }

    [Fact]
    public async Task Failed_fulfillment_task_is_listed_for_staff_and_admin()
    {
        var task = await FailedTaskAsync();
        Assert.StartsWith("Grant failed:", task.Summary, StringComparison.Ordinal);
        Assert.Equal("FulfillmentFailed", task.Kind);
        using var freja = _factory.CreateClientFor(DemoUser.Freja);
        Assert.Contains(await ListAsync(freja), t => t.TaskId == task.TaskId);
    }

    [Fact]
    public async Task Employee_gets_403()
    {
        using var anna = _factory.CreateClientFor(DemoUser.Anna);
        using var response = await anna.GetAsync(new Uri("/api/v1/tasks", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Claim_sets_in_progress_and_assignee()
    {
        var task = await FailedTaskAsync();
        using var jonas = _factory.CreateClientFor(DemoUser.Jonas);
        using var response = await PostAsync(jonas, $"/api/v1/tasks/{task.TaskId}/claim");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = (await response.Content.ReadFromJsonAsync<StaffTaskDto>(TestContext.Current.CancellationToken))!;
        Assert.Equal("InProgress", dto.Status);
        Assert.StartsWith("Jonas", dto.AssignedTo!.DisplayName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resolve_requires_action()
    {
        var task = await FailedTaskAsync();
        using var jonas = _factory.CreateClientFor(DemoUser.Jonas);
        using var response = await PostAsync(jonas, $"/api/v1/tasks/{task.TaskId}/resolve", new ResolveStaffTaskDto("AutoResolved", null));
        await AccessRequestEndpointTests.AssertProblemAsync(response, HttpStatusCode.BadRequest, "task.resolution_required");
    }

    [Fact]
    public async Task Admin_cannot_resolve()
    {
        var task = await FailedTaskAsync();
        using var freja = _factory.CreateClientFor(DemoUser.Freja);
        using var response = await PostAsync(freja, $"/api/v1/tasks/{task.TaskId}/resolve", new ResolveStaffTaskDto("Dismissed", null));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Resolved_task_cannot_be_resolved_again()
    {
        var task = await FailedTaskAsync();
        using var jonas = _factory.CreateClientFor(DemoUser.Jonas);
        using var first = await PostAsync(jonas, $"/api/v1/tasks/{task.TaskId}/resolve", new ResolveStaffTaskDto("Dismissed", "ok"));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var second = await PostAsync(jonas, $"/api/v1/tasks/{task.TaskId}/resolve", new ResolveStaffTaskDto("Dismissed", "ok"));
        await AccessRequestEndpointTests.AssertProblemAsync(second, HttpStatusCode.Conflict, "task.invalid_transition");
    }

    [Fact]
    public async Task Resolve_is_audited()
    {
        var task = await FailedTaskAsync();
        using var jonas = _factory.CreateClientFor(DemoUser.Jonas);
        (await PostAsync(jonas, $"/api/v1/tasks/{task.TaskId}/resolve", new ResolveStaffTaskDto("VerifiedInPacs", "checked"))).Dispose();

        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        Assert.True(await db.AuditEntries.AnyAsync(a => a.Action == "task.resolved" && a.SubjectId == task.TaskId.ToString(), TestContext.Current.CancellationToken));
    }
}
