using System.Net;
using System.Net.Http.Json;
using Gatehouse.Application.Credentials.RequestPinReveal;
using Gatehouse.Application.Credentials.RevealPin;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Tasks;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Gatehouse.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Credentials;

/// <summary>PIN reveal through a claimed <c>PinReveal</c> task (M8-12); test people, tasks and the extra staff role are removed.</summary>
public sealed class PinRevealTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Tenant = "nordlys";
    private static readonly Uri MyPin = new("/api/v1/me/pin", UriKind.Relative);
    private static readonly Uri RevealRequests = new("/api/v1/me/pin/reveal-requests", UriKind.Relative);

    private readonly GatehouseApiFactory _api = new(postgres);
    private readonly List<PersonId> _people = [];
    private readonly List<RoleAssignmentId> _roles = [];

    public ValueTask InitializeAsync() => _api.InitializeAsync();

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    [Fact]
    public async Task Request_opens_one_pin_reveal_task()
    {
        var ct = TestContext.Current.CancellationToken;
        var (personId, client) = await PersonWithPinAsync("4455", ct);
        var (_, noPin) = await PersonAsync(ct);
        using (client)
        using (noPin)
        {
            var first = await RequestAsync(client, ct);
            var second = await RequestAsync(client, ct);
            using var missing = await noPin.PostAsync(RevealRequests, null, ct);

            Assert.Equal(first, second);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.Contains("pin.not_set", await missing.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
        }

        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var task = await scope.ServiceProvider.GetRequiredService<TenantDbContext>().StaffTasks.AsNoTracking().SingleAsync(t => t.SubjectId == personId.Value, ct);
        Assert.Equal(StaffTaskKind.PinReveal, task.Kind);
        Assert.Null(task.SiteId);
        Assert.Equal("Person", task.SubjectType);
    }

    [Fact]
    public async Task Unclaimed_task_cannot_reveal()
    {
        var ct = TestContext.Current.CancellationToken;
        var taskId = await OpenTaskAsync("7788", ct);
        using var jonas = _api.CreateClientFor(DemoUser.Jonas);

        using var response = await RevealAsync(jonas, taskId, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("auth.forbidden", await response.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Other_staff_than_assignee_cannot_reveal()
    {
        var ct = TestContext.Current.CancellationToken;
        var taskId = await OpenTaskAsync("9182", ct);
        using var jonas = _api.CreateClientFor(DemoUser.Jonas);
        await ClaimAsync(jonas, taskId, ct);
        var (_, other) = await PersonAsync(ct, tenantWideStaff: true);
        using (other)
        {
            using var response = await RevealAsync(other, taskId, ct);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task Assignee_reveals_pin_with_no_store()
    {
        var ct = TestContext.Current.CancellationToken;
        var taskId = await OpenTaskAsync("30471", ct);
        using var jonas = _api.CreateClientFor(DemoUser.Jonas);
        await ClaimAsync(jonas, taskId, ct);

        using var response = await RevealAsync(jonas, taskId, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
        var body = (await response.Content.ReadFromJsonAsync<RevealedPinDto>(ApiJson.Options, ct))!;
        Assert.Equal("30471", body.Pin);
        Assert.Equal(30, body.MaskAfterSeconds);
    }

    [Fact]
    public async Task Reveal_is_audited_without_value()
    {
        var ct = TestContext.Current.CancellationToken;
        var (personId, client) = await PersonWithPinAsync("615243", ct);
        Guid taskId;
        using (client)
        {
            taskId = await RequestAsync(client, ct);
        }

        using var jonas = _api.CreateClientFor(DemoUser.Jonas);
        await ClaimAsync(jonas, taskId, ct);
        using (var response = await RevealAsync(jonas, taskId, ct))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var subject = personId.Value.ToString();
        var entries = await db.AuditEntries.AsNoTracking().Where(a => a.SubjectId == subject && (a.Action == "pin.revealed" || a.Action == "pin.reveal_requested" || a.Action == "pin.set")).ToListAsync(ct);
        Assert.Contains(entries, e => e.Action == "pin.revealed" && e.Payload.Contains(taskId.ToString(), StringComparison.Ordinal));
        Assert.Contains(entries, e => e.Action == "pin.reveal_requested");
        Assert.All(entries, e => Assert.DoesNotContain("615243", e.Payload, StringComparison.Ordinal));
        var since = DateTimeOffset.UtcNow.AddMinutes(-5);
        var outbox = await db.OutboxMessages.AsNoTracking().Where(m => m.OccurredAt >= since && m.Type.Contains("Pin")).Select(m => m.Payload).ToListAsync(ct);
        Assert.NotEmpty(outbox);
        Assert.All(outbox, p => Assert.DoesNotContain("615243", p, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Tenant_admin_cannot_reveal()
    {
        var ct = TestContext.Current.CancellationToken;
        var taskId = await OpenTaskAsync("2580", ct);
        using var jonas = _api.CreateClientFor(DemoUser.Jonas);
        await ClaimAsync(jonas, taskId, ct);
        using var freja = _api.CreateClientFor(DemoUser.Freja);

        using var response = await RevealAsync(freja, taskId, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<Guid> OpenTaskAsync(string pin, CancellationToken ct)
    {
        var (_, client) = await PersonWithPinAsync(pin, ct);
        using (client)
        {
            return await RequestAsync(client, ct);
        }
    }

    private static async Task<Guid> RequestAsync(HttpClient client, CancellationToken ct)
    {
        using var response = await client.PostAsync(RevealRequests, null, ct);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PinRevealRequestedDto>(ApiJson.Options, ct))!.TaskId;
    }

    private static async Task ClaimAsync(HttpClient staff, Guid taskId, CancellationToken ct)
    {
        using var response = await staff.PostAsync(new Uri($"/api/v1/tasks/{taskId}/claim", UriKind.Relative), null, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static Task<HttpResponseMessage> RevealAsync(HttpClient staff, Guid taskId, CancellationToken ct) =>
        staff.PostAsync(new Uri($"/api/v1/tasks/{taskId}/pin-reveal", UriKind.Relative), null, ct);

    private async Task<(PersonId PersonId, HttpClient Client)> PersonWithPinAsync(string pin, CancellationToken ct)
    {
        var (personId, client) = await PersonAsync(ct);
        using var response = await client.PutAsJsonAsync(MyPin, new { pin }, ct);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return (personId, client);
    }

    private async Task<(PersonId PersonId, HttpClient Client)> PersonAsync(CancellationToken ct, bool tenantWideStaff = false)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var email = $"pr-{Guid.NewGuid():N}@nordlys.test";
        var person = MappingData.Employee(email);
        person.ClearDomainEvents();
        db.Add(person);
        _people.Add(person.Id);
        if (tenantWideStaff)
        {
            var role = RoleAssignment.Create(person.Id, AssignedRole.SecurityStaff, null).Value;
            db.RoleAssignments.Add(role);
            _roles.Add(role.Id);
        }

        await db.SaveChangesAsync(ct);
        return (person.Id, _api.CreateClientFor($"sub-{Guid.NewGuid():N}", email, Tenant));
    }
}
