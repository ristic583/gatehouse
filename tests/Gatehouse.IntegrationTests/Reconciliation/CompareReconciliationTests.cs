using System.Text.Json;
using Gatehouse.Application.Common.Messaging;
using Gatehouse.Application.Connectors.Messages;
using Gatehouse.Application.Reconciliation.CompareReconciliation;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Adapters;
using Gatehouse.Connector.Contracts.Events;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Credentials;
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

/// <summary>
/// Runs the Workers consumers on staged exports of a test-owned PACS instance (own site, areas, people, mappings); everything
/// the tests add is removed in <see cref="DisposeAsync"/>.
/// </summary>
public sealed class CompareReconciliationTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Tenant = "nordlys";

    private static readonly DateTimeOffset CapturedAt = new(2026, 11, 10, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Start = CapturedAt.AddDays(-10);
    private static readonly DateTimeOffset End = CapturedAt.AddDays(30);

    private readonly GatehouseApiFactory _api = new(postgres);
    private readonly List<PersonId> _people = [];
    private readonly Dictionary<PersonId, string> _cards = [];
    private SiteId _siteId;
    private FloorId _floorId;
    private SecurityLevelId _levelId;
    private AreaId _areaA;
    private AreaId _areaB;
    private PacsInstanceId _instanceId;
    private string _levelA = string.Empty;
    private string _levelB = string.Empty;

    public async ValueTask InitializeAsync()
    {
        await _api.InitializeAsync();
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var (site, floor, level) = MappingData.Building(db);
        var owner = MappingData.Employee($"rc-owner-{Guid.NewGuid():N}@x.test");
        db.Add(owner);
        _people.Add(owner.Id);
        var areaA = NewArea(floor.Id, level.Id, owner.Id);
        var areaB = NewArea(floor.Id, level.Id, owner.Id);
        var instance = PacsInstance.Create("Recon " + Guid.NewGuid().ToString("N")[..6], PacsVendor.LegacyPacs, [site.Id]).Value;
        instance.SetFulfillmentMode(FulfillmentMode.Connector);
        instance.RecordHeartbeat(CapturedAt, "1.0.0", JsonSerializer.Serialize(
            new PacsCapabilities("LegacyPacs", "1.0.0", true, false, false, false, true, [DoorCommandKind.MomentaryUnlock], 50), ConnectorJson.Options));
        _levelA = "RC-A-" + Guid.NewGuid().ToString("N")[..6];
        _levelB = "RC-B-" + Guid.NewGuid().ToString("N")[..6];
        db.AddRange(
            areaA,
            areaB,
            instance,
            AccessLevelMapping.Create(areaA.Id, instance.Id, _levelA, "Area A").Value,
            AccessLevelMapping.Create(areaB.Id, instance.Id, _levelB, "Area B").Value);
        await db.SaveChangesAsync();
        (_siteId, _floorId, _levelId, _areaA, _areaB, _instanceId) = (site.Id, floor.Id, level.Id, areaA.Id, areaB.Id, instance.Id);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    [Fact]
    public async Task All_four_finding_types_are_detected()
    {
        var ct = TestContext.Current.CancellationToken;
        var missing = await PersonAsync(ct);
        var mismatch = await PersonAsync(ct, withCard: true);
        var extra = await PersonAsync(ct);
        var expired = await PersonAsync(ct);
        await GrantAsync(missing, _areaA, End, ct);
        await GrantAsync(mismatch, _areaA, End, ct);
        await GrantAsync(expired, _areaB, CapturedAt.AddDays(-1), ct);

        var reportId = await CompareAsync(
            [
                Row(mismatch, _levelA, "9999"),
                Row(extra, _levelA, "2002"),
                Row(expired, _levelB, "3003"),
            ],
            ct);

        var kinds = await FindingsAsync(reportId, f => new { f.Kind, f.PersonId }, ct);
        Assert.Equal(4, kinds.Count);
        Assert.Contains(kinds, k => k.Kind == FindingKind.MissingInPacs && k.PersonId == missing);
        Assert.Contains(kinds, k => k.Kind == FindingKind.Mismatch && k.PersonId == mismatch);
        Assert.Contains(kinds, k => k.Kind == FindingKind.ExtraInPacs && k.PersonId == extra);
        Assert.Contains(kinds, k => k.Kind == FindingKind.ExpiredStillPresent && k.PersonId == expired);
    }

    [Fact]
    public async Task Inactive_rows_produce_no_findings()
    {
        var ct = TestContext.Current.CancellationToken;
        var person = await PersonAsync(ct);

        var reportId = await CompareAsync(
            [
                Row(person, _levelA, "4004") with { IsActive = false },
                Row(person, _levelB, "4004") with { ValidTo = CapturedAt.AddDays(-1) },
            ],
            ct);

        Assert.Empty(await FindingsAsync(reportId, f => f.Id, ct));
        var report = await ReportAsync(reportId, ct);
        Assert.Equal(ReconciliationStatus.Completed, report.Status);
        Assert.Equal(2, report.Coverage!.IgnoredInactiveCount);
    }

    [Fact]
    public async Task Missing_and_mismatch_drift_the_grant()
    {
        var ct = TestContext.Current.CancellationToken;
        var missing = await PersonAsync(ct);
        var mismatch = await PersonAsync(ct, withCard: true);
        var missingGrant = await GrantAsync(missing, _areaA, End, ct);
        var mismatchGrant = await GrantAsync(mismatch, _areaA, End, ct);

        var reportId = await CompareAsync([Row(mismatch, _levelA, _cards[mismatch]) with { ValidTo = End.AddDays(5) }], ct);

        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var grants = await db.Fulfillments.AsNoTracking().Where(f => f.Id == missingGrant || f.Id == mismatchGrant).ToListAsync(ct);
        Assert.All(grants, g => Assert.Equal(FulfillmentStatus.Drift, g.Status));
        var findings = await FindingsAsync(reportId, f => new { f.Kind, f.FulfillmentId }, ct);
        Assert.Contains(findings, f => f.Kind == FindingKind.MissingInPacs && f.FulfillmentId == missingGrant);
        Assert.Contains(findings, f => f.Kind == FindingKind.Mismatch && f.FulfillmentId == mismatchGrant);
    }

    [Fact]
    public async Task Each_finding_opens_a_task()
    {
        var ct = TestContext.Current.CancellationToken;
        var missing = await PersonAsync(ct);
        var extra = await PersonAsync(ct);
        await GrantAsync(missing, _areaA, End, ct);

        var reportId = await CompareAsync([Row(extra, _levelB, "6006")], ct);

        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var findings = await db.ReconciliationFindings.AsNoTracking().Where(f => f.ReportId == reportId).ToListAsync(ct);
        Assert.Equal(2, findings.Count);
        foreach (var finding in findings)
        {
            Assert.NotNull(finding.StaffTaskId);
            var task = await db.StaffTasks.AsNoTracking().SingleAsync(t => t.Id == finding.StaffTaskId, ct);
            Assert.Equal(StaffTaskKind.ReconciliationFinding, task.Kind);
            Assert.Equal(CompareReconciliationHandler.TaskSubjectType, task.SubjectType);
            Assert.Equal(finding.Id.Value, task.SubjectId);
            Assert.Equal(_siteId, task.SiteId);
        }
    }

    [Fact]
    public async Task Repeated_finding_reuses_open_task()
    {
        var ct = TestContext.Current.CancellationToken;
        var missing = await PersonAsync(ct);
        await GrantAsync(missing, _areaA, End, ct);

        var first = await CompareAsync([], ct);
        var second = await CompareAsync([], ct);

        var firstTask = Assert.Single(await FindingsAsync(first, f => f.StaffTaskId, ct));
        var secondTask = Assert.Single(await FindingsAsync(second, f => f.StaffTaskId, ct));
        Assert.NotNull(firstTask);
        Assert.Equal(firstTask, secondTask);
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        Assert.Equal(1, await db.StaffTasks.CountAsync(t => t.SiteId == _siteId && t.Kind == StaffTaskKind.ReconciliationFinding, ct));
    }

    [Fact]
    public async Task Report_records_captured_compared_and_coverage()
    {
        var ct = TestContext.Current.CancellationToken;
        var inSync = await PersonAsync(ct, withCard: true);
        var missing = await PersonAsync(ct);
        await GrantAsync(inSync, _areaA, End, ct);
        await GrantAsync(missing, _areaB, End, ct);

        var reportId = await CompareAsync([Row(inSync, _levelA, _cards[inSync]), Row(inSync, _levelB, _cards[inSync]) with { IsActive = false }], ct);

        var report = await ReportAsync(reportId, ct);
        Assert.Equal(ReconciliationStatus.Completed, report.Status);
        Assert.Equal(CapturedAt, report.CapturedAt);
        Assert.NotNull(report.ComparedAt);
        var coverage = report.Coverage!;
        Assert.Equal([_siteId.Value], coverage.SiteIds);
        Assert.Equal(new[] { _areaA.Value, _areaB.Value }.Order(), coverage.AreaIds);
        Assert.Equal(new[] { _levelA, _levelB }.Order(StringComparer.Ordinal), coverage.VendorAccessLevelRefs);
        Assert.Equal(["AccessAssignment"], coverage.RecordTypes);
        Assert.Equal(2, coverage.ExportedRecordCount);
        Assert.Equal(2, coverage.ComparedRecordCount);
        Assert.Equal(1, coverage.InSyncCount);
        Assert.Equal(1, coverage.IgnoredInactiveCount);
        Assert.Equal(0, coverage.SkippedInFlightCount);
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        Assert.False(await db.ReconciliationExportRecords.AnyAsync(r => r.ReportId == reportId, ct));
    }

    [Fact]
    public async Task Failed_export_ack_fails_report()
    {
        var ct = TestContext.Current.CancellationToken;
        var commandId = CommandId.New();
        ReconciliationReportId reportId;
        await using (var scope = await _api.CreateTenantScopeAsync(Tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var report = ReconciliationReport.Request(_instanceId, null, commandId, CapturedAt);
            db.ReconciliationReports.Add(report);
            scope.ServiceProvider.GetRequiredService<IOutboxWriter>().Enqueue(
                new ConnectorCommandAcked(Tenant, _instanceId, ConnectorId.New(), CapturedAt, commandId, null, AckStatus.Failed, null,
                    new AckError("pacs.export_failed", "Export failed", false), IsFinal: true),
                CommandPriority.Normal);
            await db.SaveChangesAsync(ct);
            reportId = report.Id;
        }

        await _api.DrainMessagesAsync(Tenant);

        var failed = await ReportAsync(reportId, ct);
        Assert.Equal(ReconciliationStatus.Failed, failed.Status);
        Assert.Equal("pacs.export_failed", failed.FailureCode);
    }

    [Fact]
    public async Task Completion_is_audited()
    {
        var ct = TestContext.Current.CancellationToken;
        var missing = await PersonAsync(ct);
        await GrantAsync(missing, _areaA, End, ct);

        var reportId = await CompareAsync([], ct);

        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var entry = await db.AuditEntries.AsNoTracking()
            .SingleAsync(a => a.Action == "reconciliation.completed" && a.SubjectId == reportId.Value.ToString(), ct);
        Assert.Equal("ReconciliationReport", entry.SubjectType);
        Assert.Equal("reconciliation", entry.ActorId);
        using var payload = JsonDocument.Parse(entry.Payload);
        Assert.Equal(1, payload.RootElement.GetProperty("findingCount").GetInt32());
    }

    private static Area NewArea(FloorId floorId, SecurityLevelId levelId, PersonId ownerId) =>
        Area.Create(floorId, "Recon " + Guid.NewGuid().ToString("N")[..6], "RC-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(), MappingData.Rect(0, 0, 10, 10), levelId, ownerId, PersonType.Employee).Value;

    private static StagedRow Row(PersonId person, string levelRef, string card) =>
        new(person.Value.ToString(), card, levelRef, Start, End, IsActive: true);

    // A new person; with an Active card (unique number, kept in _cards) when asked.
    private async Task<PersonId> PersonAsync(CancellationToken ct, bool withCard = false)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var person = MappingData.Employee($"rc-{Guid.NewGuid():N}@x.test");
        db.Add(person);
        if (withCard)
        {
            var number = Random.Shared.NextInt64(1_000_000_000, 9_999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);
            db.Add(Card.CreateActive(person.Id, number, CapturedAt).Value);
            _cards[person.Id] = number;
        }

        await db.SaveChangesAsync(ct);
        _people.Add(person.Id);
        return person.Id;
    }

    // A Confirmed grant of an Approved entitlement on the test instance.
    private async Task<FulfillmentId> GrantAsync(PersonId person, AreaId area, DateTimeOffset effectiveEnd, CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var entitlement = Entitlement.Create(person, area, person, Start, effectiveEnd, effectiveEnd, "Reconciliation test", [new StepPlan(ApproverKind.AreaOwner, null)], Start);
        var grant = Domain.Fulfillment.Fulfillment.Create(entitlement.Id, _instanceId, FulfillmentKind.Grant, CommandPriority.Normal, Start);
        entitlement.ClearDomainEvents();
        grant.ClearDomainEvents();
        db.AddRange(entitlement, grant);
        db.Entry(entitlement).Property(e => e.Status).CurrentValue = EntitlementStatus.Approved;
        db.Entry(grant).Property(f => f.Status).CurrentValue = FulfillmentStatus.Confirmed;
        await db.SaveChangesAsync(ct);
        return grant.Id;
    }

    // Stages the rows as a complete export of a Comparing report, publishes RecordExportReceived and runs the consumers.
    private async Task<ReconciliationReportId> CompareAsync(IReadOnlyList<StagedRow> rows, CancellationToken ct)
    {
        ReconciliationReportId reportId;
        await using (var scope = await _api.CreateTenantScopeAsync(Tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var report = ReconciliationReport.Request(_instanceId, null, CommandId.New(), CapturedAt);
            report.AcceptChunk(0, CapturedAt, isLast: true);
            db.ReconciliationReports.Add(report);
            for (var i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                db.ReconciliationExportRecords.Add(new ReconciliationExportRecord(
                    report.Id, 0, i, "AccessAssignment", "pacs-" + i, r.PersonRef, r.Card, r.LevelRef, r.ValidFrom, r.ValidTo, r.IsActive));
            }

            scope.ServiceProvider.GetRequiredService<IOutboxWriter>().Enqueue(
                new RecordExportReceived(Tenant, _instanceId, ConnectorId.New(), CapturedAt, report.Id.Value, 1, rows.Count, CapturedAt),
                CommandPriority.Normal);
            await db.SaveChangesAsync(ct);
            reportId = report.Id;
        }

        await _api.DrainMessagesAsync(Tenant);
        Assert.Equal(ReconciliationStatus.Completed, (await ReportAsync(reportId, ct)).Status);
        return reportId;
    }

    private async Task<ReconciliationReport> ReportAsync(ReconciliationReportId id, CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        return await db.ReconciliationReports.AsNoTracking().SingleAsync(r => r.Id == id, ct);
    }

    private async Task<List<T>> FindingsAsync<T>(ReconciliationReportId id, System.Linq.Expressions.Expression<Func<ReconciliationFinding, T>> select, CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        return await db.ReconciliationFindings.AsNoTracking().Where(f => f.ReportId == id).Select(select).ToListAsync(ct);
    }

    private sealed record StagedRow(string PersonRef, string Card, string LevelRef, DateTimeOffset? ValidFrom, DateTimeOffset? ValidTo, bool IsActive);
}
