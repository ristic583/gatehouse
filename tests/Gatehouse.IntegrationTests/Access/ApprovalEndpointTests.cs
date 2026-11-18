extern alias GatehouseApi;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gatehouse.Application.Access.Dtos;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Gatehouse.IntegrationTests.Access.AccessRequestEndpointTests;

namespace Gatehouse.IntegrationTests.Access;

public sealed class ApprovalEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    public static readonly Uri Inbox = new("/api/v1/approvals/inbox", UriKind.Relative);

    private readonly GatehouseApiFactory _factory = new(postgres);

    public ValueTask InitializeAsync() => _factory.InitializeAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    public static Task<HttpResponseMessage> DecideAsync(HttpClient client, Guid stepId, string action, string? comment = null) =>
        client.PostAsJsonAsync(new Uri($"/api/v1/approvals/{stepId}/{action}", UriKind.Relative), new DecideStepDto(comment), TestContext.Current.CancellationToken);

    public static async Task<List<ApprovalInboxItemDto>> InboxAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<ApprovalInboxItemDto>>(Inbox, TestContext.Current.CancellationToken))!;

    public static async Task<Guid> SeededLabStepAsync(HttpClient anna)
    {
        var mine = await anna.GetFromJsonAsync<List<AccessRequestDto>>(Requests, TestContext.Current.CancellationToken);
        return mine!.Single(r => r.Area.Name == "Lab").Steps.Single(s => s.Status == "Pending").StepId;
    }

    public static HttpClient ClientFor(WebApplicationFactory<GatehouseApi::Program> app, DemoUser user)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(user);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, user.Subject);
        client.DefaultRequestHeaders.Add(TestAuthHandler.EmailHeader, user.Email);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, user.Tenant);
        return client;
    }

    [Fact]
    public async Task Manager_then_owner_approval_leaves_area_pending_not_green()
    {
        var ct = TestContext.Current.CancellationToken;
        using var anna = _factory.CreateClientFor(DemoUser.Anna);
        using var mikkel = _factory.CreateClientFor(DemoUser.Mikkel);
        using var sofie = _factory.CreateClientFor(DemoUser.Sofie);
        using var created = await PostRequestAsync(_factory, anna, "nordlys", "CPH-SERVER");
        var request = await CreatedAsync(created);

        using (var first = await DecideAsync(mikkel, request.Steps[0].StepId, "approve"))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        using var second = await DecideAsync(sofie, request.Steps[1].StepId, "approve", "ok");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var approved = await second.Content.ReadFromJsonAsync<AccessRequestDto>(ct);
        Assert.Equal("Approved", approved!.Status);
        Assert.Equal("Desired", approved.FulfillmentStatus);

        var floorId = await AccessMapEndpointTests.FloorIdAsync(_factory, "nordlys", "CPH-HQ", 2);
        var map = await anna.GetFromJsonAsync<FloorAccessMapDto>(new Uri($"/api/v1/floors/{floorId}/access-map", UriKind.Relative), ct);
        var server = map!.Areas.Single(a => a.Code == "CPH-SERVER");
        Assert.Equal(("Pending", "AwaitingFulfillment"), (server.State, server.Reason));

        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var id = new Gatehouse.Domain.Access.EntitlementId(request.EntitlementId);
        var fulfillment = Assert.Single(await db.Fulfillments.Where(f => f.EntitlementId == id).ToListAsync(ct));
        Assert.Equal(FulfillmentStatus.Desired, fulfillment.Status);
        var types = await db.OutboxMessages.Select(m => m.Type).ToListAsync(ct);
        Assert.Contains("EntitlementApproved", types);
        Assert.Contains("FulfillmentDesired", types);
    }

    [Fact]
    public async Task Requester_cannot_approve_own_request()
    {
        var annaId = await PersonIdAsync(_factory, "nordlys", DemoUser.Anna.Email);
        using var mikkel = _factory.CreateClientFor(DemoUser.Mikkel);
        using var created = await PostRequestAsync(_factory, mikkel, "nordlys", "CPH-OFFICE-0", annaId);
        var request = await CreatedAsync(created);

        using var response = await DecideAsync(mikkel, request.Steps[0].StepId, "approve");
        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "approval.self_approval_forbidden");
    }

    [Fact]
    public async Task Subject_who_owns_area_cannot_approve()
    {
        await ChangeOwnerAsync("CPH-LAB", DemoUser.Erik.Email, add: true);
        try
        {
            using var sofie = _factory.CreateClientFor(DemoUser.Sofie);
            using var created = await PostRequestAsync(_factory, sofie, "nordlys", "CPH-LAB");
            var request = await CreatedAsync(created);

            using var response = await DecideAsync(sofie, request.Steps[0].StepId, "approve");
            await AssertProblemAsync(response, HttpStatusCode.Forbidden, "approval.self_approval_forbidden");
        }
        finally
        {
            await ChangeOwnerAsync("CPH-LAB", DemoUser.Erik.Email, add: false);
        }
    }

    [Fact]
    public async Task Tenant_admin_cannot_approve()
    {
        using var anna = _factory.CreateClientFor(DemoUser.Anna);
        using var freja = _factory.CreateClientFor(DemoUser.Freja);

        using var response = await DecideAsync(freja, await SeededLabStepAsync(anna), "approve");
        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "approval.not_an_approver");
    }

    [Fact]
    public async Task Owner_of_other_area_cannot_approve()
    {
        using var anna = _factory.CreateClientFor(DemoUser.Anna);
        using var erik = _factory.CreateClientFor(DemoUser.Erik);

        using var response = await DecideAsync(erik, await SeededLabStepAsync(anna), "approve");
        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "approval.not_an_approver");
    }

    [Fact]
    public async Task Second_step_cannot_be_approved_before_first()
    {
        using var anna = _factory.CreateClientFor(DemoUser.Anna);
        using var sofie = _factory.CreateClientFor(DemoUser.Sofie);
        using var created = await PostRequestAsync(_factory, anna, "nordlys", "CPH-SERVER");
        var request = await CreatedAsync(created);

        using var response = await DecideAsync(sofie, request.Steps[1].StepId, "approve");
        await AssertProblemAsync(response, HttpStatusCode.Conflict, "approval.step_not_pending");
    }

    [Fact]
    public async Task Reject_requires_comment()
    {
        using var anna = _factory.CreateClientFor(DemoUser.Anna);
        using var sofie = _factory.CreateClientFor(DemoUser.Sofie);
        var stepId = await SeededLabStepAsync(anna);

        using (var response = await DecideAsync(sofie, stepId, "reject"))
        {
            await AssertProblemAsync(response, HttpStatusCode.BadRequest, "approval.comment_required");
        }

        using var rejected = await DecideAsync(sofie, stepId, "reject", "Lab is full this quarter");
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Equal("Rejected", (await rejected.Content.ReadFromJsonAsync<AccessRequestDto>(TestContext.Current.CancellationToken))!.Status);
    }

    [Fact]
    public async Task Inbox_never_contains_own_requests()
    {
        var annaId = await PersonIdAsync(_factory, "nordlys", DemoUser.Anna.Email);
        using var mikkel = _factory.CreateClientFor(DemoUser.Mikkel);
        using var created = await PostRequestAsync(_factory, mikkel, "nordlys", "CPH-SERVER", annaId);
        var request = await CreatedAsync(created);
        Assert.Equal("Erik Møller", request.Steps[0].AssignedApprover?.DisplayName);

        var inbox = await InboxAsync(mikkel);
        Assert.Contains(inbox, i => i.Area.Name == "Vault");
        Assert.DoesNotContain(inbox, i => i.EntitlementId == request.EntitlementId);
        Assert.All(inbox, i => Assert.NotEqual("Mikkel Holm", i.RequestedBy.DisplayName));

        using var erik = _factory.CreateClientFor(DemoUser.Erik);
        Assert.Contains(await InboxAsync(erik), i => i.EntitlementId == request.EntitlementId);
    }

    [Fact]
    public async Task Pending_request_past_its_window_leaves_the_inbox()
    {
        using var current = ClientFor(_factory, DemoUser.Sofie);
        Assert.Single(await InboxAsync(current), i => i.Area.Name == "Lab");

        // M11-17: once the window has ended the request can no longer be approved (the lifecycle run then expires it).
        var clock = new ShiftedTimeProvider(TimeSpan.FromDays(400));
        await using var app = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(clock)));
        using var sofie = ClientFor(app, DemoUser.Sofie);

        Assert.DoesNotContain(await InboxAsync(sofie), i => i.Area.Name == "Lab");
    }

    [Fact]
    public async Task Approve_by_two_approvers_at_once_returns_one_success_and_one_conflict()
    {
        // M11-41 load test: the race loser hit the fulfillment unique index and got 500 instead of 409.
        var ct = TestContext.Current.CancellationToken;
        await ChangeOwnerAsync("CPH-LAB", DemoUser.Mikkel.Email, add: true);
        using var anna = _factory.CreateClientFor(DemoUser.Anna);
        using var sofie = _factory.CreateClientFor(DemoUser.Sofie);
        using var mikkel = _factory.CreateClientFor(DemoUser.Mikkel);
        var stepId = await SeededLabStepAsync(anna);

        using var start = new Barrier(2);
        var decisions = await Task.WhenAll(
            Task.Run(() => { start.SignalAndWait(ct); return DecideAsync(sofie, stepId, "approve"); }, ct),
            Task.Run(() => { start.SignalAndWait(ct); return DecideAsync(mikkel, stepId, "approve"); }, ct));
        try
        {
            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], decisions.Select(d => d.StatusCode).Order().ToArray());
            // In the race the loser gets step_not_pending; if the scheduler runs the two one after the other, the loser sees the
            // already approved entitlement and the domain answers invalid_transition. Both are 409; a 500 is the bug.
            var loser = await decisions.Single(d => d.StatusCode != HttpStatusCode.OK).Content.ReadFromJsonAsync<JsonElement>(ct);
            var code = loser.GetProperty("code").GetString();
            Assert.True(code is "approval.step_not_pending" or "entitlement.invalid_transition", code);
        }
        finally
        {
            foreach (var decision in decisions)
            {
                decision.Dispose();
            }
        }

        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var step = new Gatehouse.Domain.Access.ApprovalStepId(stepId);
        var entitlement = await db.Entitlements.AsNoTracking().SingleAsync(e => e.Steps.Any(s => s.Id == step), ct);
        var instances = await db.AccessLevelMappings.Where(m => m.AreaId == entitlement.AreaId)
            .Select(m => m.PacsInstanceId).Distinct().CountAsync(ct);
        var fulfillments = await db.Fulfillments.Where(f => f.EntitlementId == entitlement.Id).ToListAsync(ct);
        Assert.True(instances > 0);
        Assert.Equal(instances, fulfillments.Count);
        Assert.Equal(instances, fulfillments.Select(f => f.PacsInstanceId).Distinct().Count());
        var subject = entitlement.Id.Value.ToString();
        var actions = await db.AuditEntries.Where(a => a.SubjectId == subject).Select(a => a.Action).ToListAsync(ct);
        Assert.Single(actions, a => a == "approval.granted");
        Assert.Single(actions, a => a == "entitlement.approved");
        var approvedMessages = await db.OutboxMessages.Where(m => m.Type == "EntitlementApproved").Select(m => m.Payload).ToListAsync(ct);
        Assert.Single(approvedMessages, p => p.Contains(subject, StringComparison.Ordinal));
    }

    private async Task ChangeOwnerAsync(string areaCode, string email, bool add)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var area = await db.Areas.SingleAsync(a => a.Code == areaCode, ct);
        var person = await db.Persons.SingleAsync(p => p.Email == email, ct);
        var result = add ? area.AddOwner(person.Id, person.Type) : area.RemoveOwner(person.Id);
        Assert.True(result.IsSuccess);
        await db.SaveChangesAsync(ct);
    }

    private sealed class ShiftedTimeProvider(TimeSpan shift) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + shift;
    }
}
