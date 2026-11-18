using System.Net;
using Gatehouse.Domain.Audit;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Access;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Audit;

public sealed class AccessAuditTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatehouseApiFactory _factory = new(postgres);

    public ValueTask InitializeAsync() => _factory.InitializeAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Request_writes_requested_entry()
    {
        using var anna = _factory.CreateClientFor(DemoUser.Anna);
        using var created = await AccessRequestEndpointTests.PostRequestAsync(_factory, anna, "nordlys", "CPH-SERVER");
        var request = await AccessRequestEndpointTests.CreatedAsync(created);

        var entry = Assert.Single(await EntriesAsync(request.EntitlementId));
        Assert.Equal("entitlement.requested", entry.Action);
        Assert.Equal(AuditActorKind.Person, entry.ActorKind);
        Assert.True(entry.HasValidHash());
    }

    [Fact]
    public async Task Two_step_approval_writes_granted_entries_and_approved()
    {
        using var anna = _factory.CreateClientFor(DemoUser.Anna);
        using var mikkel = _factory.CreateClientFor(DemoUser.Mikkel);
        using var sofie = _factory.CreateClientFor(DemoUser.Sofie);
        using var created = await AccessRequestEndpointTests.PostRequestAsync(_factory, anna, "nordlys", "CPH-SERVER");
        var request = await AccessRequestEndpointTests.CreatedAsync(created);
        (await ApprovalEndpointTests.DecideAsync(mikkel, request.Steps[0].StepId, "approve")).Dispose();
        (await ApprovalEndpointTests.DecideAsync(sofie, request.Steps[1].StepId, "approve", "ok")).Dispose();

        var actions = (await EntriesAsync(request.EntitlementId)).Select(e => e.Action).ToList();
        Assert.Equal(["entitlement.requested", "approval.granted", "approval.granted", "entitlement.approved"], actions);
    }

    [Fact]
    public async Task Reject_writes_rejected_entry_with_comment()
    {
        using var anna = _factory.CreateClientFor(DemoUser.Anna);
        using var mikkel = _factory.CreateClientFor(DemoUser.Mikkel);
        using var created = await AccessRequestEndpointTests.PostRequestAsync(_factory, anna, "nordlys", "CPH-SERVER");
        var request = await AccessRequestEndpointTests.CreatedAsync(created);
        using var rejected = await ApprovalEndpointTests.DecideAsync(mikkel, request.Steps[0].StepId, "reject", "not needed");
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);

        var last = (await EntriesAsync(request.EntitlementId)).Last();
        Assert.Equal("approval.rejected", last.Action);
        Assert.Contains("not needed", last.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failed_request_writes_no_entry()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var before = await _factory.CreateTenantScopeAsync("nordlys");
        var count = await before.ServiceProvider.GetRequiredService<TenantDbContext>().AuditEntries.CountAsync(ct);

        using var anna = _factory.CreateClientFor(DemoUser.Anna);
        using var response = await AccessRequestEndpointTests.PostRequestAsync(_factory, anna, "nordlys", "CPH-LAB");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var after = await _factory.CreateTenantScopeAsync("nordlys");
        Assert.Equal(count, await after.ServiceProvider.GetRequiredService<TenantDbContext>().AuditEntries.CountAsync(ct));
    }

    private async Task<List<AuditEntry>> EntriesAsync(Guid entitlementId)
    {
        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        return await scope.ServiceProvider.GetRequiredService<TenantDbContext>().AuditEntries.AsNoTracking()
            .Where(e => e.SubjectId == entitlementId.ToString()).OrderBy(e => e.Sequence).ToListAsync(TestContext.Current.CancellationToken);
    }
}
