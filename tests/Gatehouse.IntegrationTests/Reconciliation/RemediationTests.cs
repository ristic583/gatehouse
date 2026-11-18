using System.Net;
using System.Net.Http.Json;
using Gatehouse.Application.Common.Messaging;
using Gatehouse.Application.Connectors.Messages;
using Gatehouse.Connector.Contracts.Events;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Reconciliation;
using Gatehouse.Domain.Sites;
using Gatehouse.Domain.Tasks;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Gatehouse.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Reconciliation;

/// <summary>Resolving reconciliation finding tasks (domain-model.md §2.7 "Remediation") on HQ-Copenhagen; all test rows are removed.</summary>
public sealed class RemediationTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Tenant = "nordlys";

    private readonly GatehouseApiFactory _api = new(postgres);
    private readonly List<StaffTaskId> _tasks = [];
    private readonly List<ReconciliationReportId> _reports = [];
    private readonly List<FulfillmentId> _fulfillments = [];
    private readonly List<EntitlementId> _entitlements = [];
    private readonly List<PersonId> _people = [];
    private PacsInstanceId _instanceId;
    private SiteId _siteId;
    private AreaId _areaId;

    public async ValueTask InitializeAsync()
    {
        await _api.InitializeAsync();
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        _instanceId = (await db.PacsInstances.AsNoTracking().SingleAsync(p => p.Name == "HQ-Copenhagen")).Id;
        _siteId = (await db.Sites.AsNoTracking().SingleAsync(s => s.Code == "CPH-HQ")).Id;
        _areaId = (await db.Areas.AsNoTracking().SingleAsync(a => a.Code == "CPH-VAULT")).Id;
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    [Fact]
    public async Task Remediated_repushes_drifted_grant()
    {
        var ct = TestContext.Current.CancellationToken;
        var (taskId, _, grantId) = await ArrangeAsync(FindingKind.MissingInPacs, ct);

        using var response = await ResolveAsync(taskId, "Remediated", null, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var grant = await GrantAsync(grantId!.Value, ct);
        Assert.Equal(FulfillmentStatus.Desired, grant.Status);
        Assert.Equal(2, grant.Revision);
        Assert.Equal(StaffTaskStatus.Resolved, await TaskStatusAsync(taskId, ct));
    }

    [Fact]
    public async Task Verified_in_pacs_restores_confirmed()
    {
        var ct = TestContext.Current.CancellationToken;
        var (taskId, _, grantId) = await ArrangeAsync(FindingKind.Mismatch, ct);

        using var response = await ResolveAsync(taskId, "VerifiedInPacs", null, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(FulfillmentStatus.Confirmed, (await GrantAsync(grantId!.Value, ct)).Status);
    }

    [Fact]
    public async Task Removed_from_pacs_queues_revoke_command()
    {
        var ct = TestContext.Current.CancellationToken;
        var (taskId, findingId, _) = await ArrangeAsync(FindingKind.ExtraInPacs, ct);

        using var response = await ResolveAsync(taskId, "RemovedFromPacs", null, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var command = await db.ConnectorCommands.AsNoTracking().SingleAsync(c => c.IdempotencyKey == $"RevokeAccess:{findingId.Value}:1", ct);
        Assert.Equal("RevokeAccess", command.Type);
        Assert.Equal($"finding:{findingId.Value}", command.PartitionKey);
        Assert.Equal(_instanceId, command.PacsInstanceId);
        Assert.Null(command.FulfillmentId);
        Assert.Equal(StaffTaskStatus.InProgress, await TaskStatusAsync(taskId, ct));
    }

    [Fact]
    public async Task Removed_from_pacs_resolves_task_only_after_confirmed_removal()
    {
        var ct = TestContext.Current.CancellationToken;
        var (taskId, findingId, _) = await ArrangeAsync(FindingKind.ExtraInPacs, ct);
        using (var first = await ResolveAsync(taskId, "RemovedFromPacs", null, ct))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        await AckAsync($"RevokeAccess:{findingId.Value}:1", AckStatus.Failed, ct);
        Assert.Equal(StaffTaskStatus.InProgress, await TaskStatusAsync(taskId, ct));

        using (var retry = await ResolveAsync(taskId, "RemovedFromPacs", null, ct))
        {
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        }

        await AckAsync($"RevokeAccess:{findingId.Value}:2", AckStatus.Applied, ct);
        Assert.Equal(StaffTaskStatus.Resolved, await TaskStatusAsync(taskId, ct));
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        Assert.True(await db.AuditEntries.AsNoTracking().AnyAsync(a => a.Action == "reconciliation.removal_failed" && a.SubjectId == findingId.Value.ToString(), ct));
        Assert.True(await db.AuditEntries.AsNoTracking().AnyAsync(a => a.Action == "reconciliation.removal_confirmed" && a.SubjectId == findingId.Value.ToString(), ct));
    }

    [Fact]
    public async Task Removed_from_pacs_on_expired_still_present_queues_revoke_command()
    {
        var ct = TestContext.Current.CancellationToken;
        var (taskId, findingId, _) = await ArrangeAsync(FindingKind.ExpiredStillPresent, ct);

        using var response = await ResolveAsync(taskId, "RemovedFromPacs", null, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var command = await db.ConnectorCommands.AsNoTracking().SingleAsync(c => c.IdempotencyKey == $"RevokeAccess:{findingId.Value}:1", ct);
        Assert.Equal("RevokeAccess", command.Type);
        Assert.Equal(_instanceId, command.PacsInstanceId);
    }

    [Fact]
    public async Task Dismiss_requires_note()
    {
        var ct = TestContext.Current.CancellationToken;
        var (taskId, _, _) = await ArrangeAsync(FindingKind.ExpiredStillPresent, ct);

        using var withoutNote = await ResolveAsync(taskId, "Dismissed", null, ct);
        using var withNote = await ResolveAsync(taskId, "Dismissed", "Known contractor badge, removed by vendor", ct);

        Assert.Equal(HttpStatusCode.BadRequest, withoutNote.StatusCode);
        Assert.Contains("task.resolution_required", await withoutNote.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, withNote.StatusCode);
    }

    [Fact]
    public async Task Wrong_action_for_kind_is_400()
    {
        var ct = TestContext.Current.CancellationToken;
        var (extraTask, _, _) = await ArrangeAsync(FindingKind.ExtraInPacs, ct);
        var (missingTask, _, _) = await ArrangeAsync(FindingKind.MissingInPacs, ct);

        using var remediateExtra = await ResolveAsync(extraTask, "Remediated", null, ct);
        using var removeMissing = await ResolveAsync(missingTask, "RemovedFromPacs", null, ct);
        using var retryFinding = await ResolveAsync(missingTask, "Retried", null, ct);

        Assert.Equal(HttpStatusCode.BadRequest, remediateExtra.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, removeMissing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, retryFinding.StatusCode);
        Assert.Equal(StaffTaskStatus.Open, await TaskStatusAsync(extraTask, ct));
    }

    [Fact]
    public async Task Remediation_is_audited()
    {
        var ct = TestContext.Current.CancellationToken;
        var (taskId, findingId, _) = await ArrangeAsync(FindingKind.MissingInPacs, ct);

        using var response = await ResolveAsync(taskId, "Remediated", null, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var entry = await db.AuditEntries.AsNoTracking()
            .SingleAsync(a => a.Action == "reconciliation.remediated" && a.SubjectId == findingId.Value.ToString(), ct);
        Assert.Equal("ReconciliationFinding", entry.SubjectType);
        Assert.Contains("Remediated", entry.Payload, StringComparison.Ordinal);
    }

    // A completed report with one finding of the given kind and its open task; grant kinds also get a drifted grant.
    private async Task<(StaffTaskId TaskId, ReconciliationFindingId FindingId, FulfillmentId? GrantId)> ArrangeAsync(FindingKind kind, CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var now = DateTimeOffset.UtcNow;
        var person = MappingData.Employee($"rm-{Guid.NewGuid():N}@x.test");
        db.Add(person);
        _people.Add(person.Id);

        FulfillmentId? grantId = null;
        EntitlementId? entitlementId = null;
        if (kind is FindingKind.MissingInPacs or FindingKind.Mismatch)
        {
            var entitlement = Entitlement.Create(person.Id, _areaId, person.Id, now.AddDays(-1), now.AddDays(30), now.AddDays(30), "Remediation test",
                [new StepPlan(ApproverKind.AreaOwner, null)], now);
            var grant = Domain.Fulfillment.Fulfillment.Create(entitlement.Id, _instanceId, FulfillmentKind.Grant, CommandPriority.Normal, now);
            entitlement.ClearDomainEvents();
            grant.ClearDomainEvents();
            db.AddRange(entitlement, grant);
            db.Entry(entitlement).Property(e => e.Status).CurrentValue = EntitlementStatus.Approved;
            db.Entry(grant).Property(f => f.Status).CurrentValue = FulfillmentStatus.Drift;
            (grantId, entitlementId) = (grant.Id, entitlement.Id);
            _fulfillments.Add(grant.Id);
            _entitlements.Add(entitlement.Id);
        }

        var report = ReconciliationReport.Request(_instanceId, null, CommandId.New(), now);
        report.Fail("connector.failed", "Test fixture", now);
        var finding = ReconciliationFinding.Create(
            report.Id, kind, kind == FindingKind.ExtraInPacs ? null : person.Id, _areaId, entitlementId, grantId, "pacs-1",
            kind == FindingKind.ExtraInPacs ? "unknown-ref" : person.Id.Value.ToString(), "LVL-VAULT", "5555666677778888",
            new FindingDetails([], null, null), now);
        var task = StaffTask.Open(StaffTaskKind.ReconciliationFinding, _siteId, "ReconciliationFinding", finding.Id.Value, now);
        finding.AttachTask(task.Id);
        report.ClearDomainEvents();
        db.AddRange(report, finding, task);
        await db.SaveChangesAsync(ct);
        _reports.Add(report.Id);
        _tasks.Add(task.Id);
        return (task.Id, finding.Id, grantId);
    }

    // A final connector ack for the command with this idempotency key, through the outbox and consumers.
    private async Task AckAsync(string idempotencyKey, AckStatus status, CancellationToken ct)
    {
        await using (var scope = await _api.CreateTenantScopeAsync(Tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var command = await db.ConnectorCommands.AsNoTracking().SingleAsync(c => c.IdempotencyKey == idempotencyKey, ct);
            var error = status == AckStatus.Applied ? null : new AckError("cloudpacs.forbidden", "Refused", false);
            scope.ServiceProvider.GetRequiredService<IOutboxWriter>().Enqueue(
                new ConnectorCommandAcked(Tenant, _instanceId, ConnectorId.New(), DateTimeOffset.UtcNow, command.Id, null, status, null, error, true),
                CommandPriority.Normal);
            await db.SaveChangesAsync(ct);
        }

        await _api.DrainMessagesAsync(Tenant);
    }

    private async Task<HttpResponseMessage> ResolveAsync(StaffTaskId taskId, string action, string? note, CancellationToken ct)
    {
        using var jonas = _api.CreateClientFor(DemoUser.Jonas);
        return await jonas.PostAsJsonAsync(new Uri($"/api/v1/tasks/{taskId.Value}/resolve", UriKind.Relative), new { action, note }, ct);
    }

    private async Task<Domain.Fulfillment.Fulfillment> GrantAsync(FulfillmentId id, CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        return await db.Fulfillments.AsNoTracking().SingleAsync(f => f.Id == id, ct);
    }

    private async Task<StaffTaskStatus> TaskStatusAsync(StaffTaskId id, CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        return await db.StaffTasks.AsNoTracking().Where(t => t.Id == id).Select(t => t.Status).SingleAsync(ct);
    }
}
