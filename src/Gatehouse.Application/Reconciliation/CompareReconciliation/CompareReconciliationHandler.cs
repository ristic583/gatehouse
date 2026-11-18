using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Messaging;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Connectors;
using Gatehouse.Application.Reconciliation.RequestReconciliation;
using Gatehouse.Application.Tasks;
using Gatehouse.Connector.Contracts.Adapters;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Credentials;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Reconciliation;
using Gatehouse.Domain.Reconciliation.Events;
using Gatehouse.Domain.Sites;
using Gatehouse.Domain.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Gatehouse.Application.Reconciliation.CompareReconciliation;

/// <summary>
/// Completes a <c>Comparing</c> report (domain-model.md §2.7 "Completion"): compares the staged export with confirmed state,
/// stores findings, drifts grants, opens or reuses tasks, completes the report and deletes the staged rows. The consumer saves once.
/// </summary>
public sealed partial class CompareReconciliationHandler(
    ITenantDb db,
    StaffTaskService tasks,
    IAuditLog audit,
    IOutboxWriter outbox,
    TimeProvider time,
    ILogger<CompareReconciliationHandler> logger)
{
    public const string TaskSubjectType = "ReconciliationFinding";

    private static readonly AuditActor Actor = AuditActor.ForSystem("reconciliation");
    private static readonly string AccessAssignment = nameof(RecordType.AccessAssignment);

    public async Task<Result> HandleAsync(ReconciliationReportId reportId, CancellationToken ct)
    {
        var report = await db.ReconciliationReports.FirstOrDefaultAsync(r => r.Id == reportId, ct);
        if (report is null || report.Status != ReconciliationStatus.Comparing || report.CapturedAt is not { } capturedAt)
        {
            return Result.Success();
        }

        var instance = await db.PacsInstances.AsNoTracking().FirstAsync(p => p.Id == report.PacsInstanceId, ct);
        var now = time.GetUtcNow();

        var mappings = await db.AccessLevelMappings.AsNoTracking()
            .Where(m => m.PacsInstanceId == instance.Id)
            .Select(m => new { m.AreaId, m.VendorAccessLevelRef })
            .ToListAsync(ct);
        var areaByLevelRef = mappings
            .GroupBy(m => m.VendorAccessLevelRef, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(m => m.AreaId).OrderBy(a => a.Value).First(), StringComparer.Ordinal);

        var staged = await db.ReconciliationExportRecords.Where(r => r.ReportId == reportId).ToListAsync(ct);
        var rows = staged
            .Where(r => r.RecordType == AccessAssignment && r.VendorAccessLevelRef is not null)
            .OrderBy(r => r.ChunkIndex).ThenBy(r => r.Ordinal)
            .Select(r => new ExportedAssignment(
                r.PacsRef, r.PersonRef, r.CardNumber, r.VendorAccessLevelRef!, r.ValidFrom, r.ValidTo, r.IsActive))
            .ToList();

        var grants = await LoadGrantsAsync(instance.Id, ct);
        var inFlight = await LoadInFlightRevokesAsync(instance.Id, ct);
        var activeCards = await LoadActiveCardsAsync(rows, grants, ct);
        var nativeValidity = PacsCapabilityGaps.From(instance.CapabilitiesJson) is { } gaps
            && !gaps.Contains(PacsCapabilityGaps.NativeValidity);

        var outcome = ReconciliationComparer.Compare(
            new ReconciliationInput(capturedAt, nativeValidity, grants, inFlight, activeCards, areaByLevelRef, rows));

        await StoreFindingsAsync(report, instance, outcome.Findings, now, ct);

        var coverage = new ReconciliationCoverage(
            [.. instance.SiteIds.Select(s => s.Value).Order()],
            [.. areaByLevelRef.Values.Select(a => a.Value).Distinct().Order()],
            [.. areaByLevelRef.Keys.Order(StringComparer.Ordinal)],
            [AccessAssignment],
            staged.Count,
            outcome.ComparedRecordCount,
            outcome.InSyncCount,
            outcome.SkippedInFlightCount,
            outcome.IgnoredInactiveCount);
        var completed = report.Complete(coverage, outcome.Findings.Count, now);
        if (completed.IsFailure)
        {
            return completed;
        }

        audit.Record(Actor, "reconciliation.completed", ReconciliationStarter.AuditSubject, report.Id.Value.ToString(), new
        {
            pacsInstanceId = instance.Id.Value,
            exportedRecordCount = coverage.ExportedRecordCount,
            comparedRecordCount = coverage.ComparedRecordCount,
            inSyncCount = coverage.InSyncCount,
            findingCount = outcome.Findings.Count,
        });
        db.ReconciliationExportRecords.RemoveRange(staged);
        LogCompleted(report.Id.Value, staged.Count, outcome.ComparedRecordCount, outcome.InSyncCount, outcome.Findings.Count);
        return Result.Success();
    }

    private async Task<List<GrantState>> LoadGrantsAsync(PacsInstanceId instanceId, CancellationToken ct) =>
        await (from f in db.Fulfillments.AsNoTracking()
               join e in db.Entitlements.AsNoTracking() on f.EntitlementId equals e.Id
               join m in db.AccessLevelMappings.AsNoTracking()
                   on new { e.AreaId, f.PacsInstanceId } equals new { m.AreaId, m.PacsInstanceId }
               where f.PacsInstanceId == instanceId
                   && f.Kind == FulfillmentKind.Grant
                   && f.Status != FulfillmentStatus.Superseded
                   && (e.Status == EntitlementStatus.Approved || e.Status == EntitlementStatus.Expired)
               select new GrantState(
                   f.Id, e.Id, e.PersonId, e.AreaId, m.VendorAccessLevelRef, f.Status, e.Status, e.RequestedStart, e.EffectiveEnd))
            .ToListAsync(ct);

    private async Task<List<InFlightKey>> LoadInFlightRevokesAsync(PacsInstanceId instanceId, CancellationToken ct)
    {
        var keys = await (from f in db.Fulfillments.AsNoTracking()
                          join e in db.Entitlements.AsNoTracking() on f.EntitlementId equals e.Id
                          join m in db.AccessLevelMappings.AsNoTracking()
                              on new { e.AreaId, f.PacsInstanceId } equals new { m.AreaId, m.PacsInstanceId }
                          where f.PacsInstanceId == instanceId
                              && f.Kind == FulfillmentKind.Revoke
                              && f.Status != FulfillmentStatus.Confirmed
                              && f.Status != FulfillmentStatus.Superseded
                          select new { e.PersonId, m.VendorAccessLevelRef })
            .ToListAsync(ct);
        return [.. keys.Select(k => new InFlightKey(k.PersonId.Value.ToString(), k.VendorAccessLevelRef))];
    }

    // Every known person among the export's person refs and the grants; an empty set = known person without an active card.
    private async Task<Dictionary<string, IReadOnlySet<string>>> LoadActiveCardsAsync(
        List<ExportedAssignment> rows,
        List<GrantState> grants,
        CancellationToken ct)
    {
        var candidates = rows
            .Select(r => Guid.TryParse(r.PersonRef, out var id) ? new PersonId(id) : (PersonId?)null)
            .OfType<PersonId>()
            .Concat(grants.Select(g => g.PersonId))
            .Distinct()
            .ToList();
        var known = await db.Persons.AsNoTracking().Where(p => candidates.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct);
        var cards = await db.Cards.AsNoTracking()
            .Where(c => known.Contains(c.PersonId) && c.State == CardState.Active)
            .Select(c => new { c.PersonId, c.CardNumber })
            .ToListAsync(ct);
        var byPerson = cards.ToLookup(c => c.PersonId, c => c.CardNumber);
        return known.ToDictionary(
            p => p.Value.ToString(),
            p => (IReadOnlySet<string>)byPerson[p].ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    private async Task StoreFindingsAsync(
        ReconciliationReport report,
        PacsInstance instance,
        IReadOnlyList<FindingDraft> drafts,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (drafts.Count == 0)
        {
            return;
        }

        var areaIds = drafts.Where(d => d.AreaId is not null).Select(d => d.AreaId!.Value).Distinct().ToList();
        var siteByArea = await (from a in db.Areas.AsNoTracking()
                                join f in db.Floors.AsNoTracking() on a.FloorId equals f.Id
                                where areaIds.Contains(a.Id)
                                select new { a.Id, f.SiteId })
            .ToDictionaryAsync(x => x.Id, x => x.SiteId, ct);
        SiteId? fallbackSite = instance.SiteIds.Count == 1 ? instance.SiteIds.First() : null;

        var openTasks = await OpenTasksByKeyAsync(instance.Id, ct);

        var driftIds = drafts.Where(d => d.Kind is FindingKind.MissingInPacs or FindingKind.Mismatch && d.FulfillmentId is not null)
            .Select(d => d.FulfillmentId!.Value).Distinct().ToList();
        var fulfillments = await db.Fulfillments.Where(f => driftIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, ct);

        foreach (var draft in drafts)
        {
            var finding = ReconciliationFinding.Create(
                report.Id, draft.Kind, draft.PersonId, draft.AreaId, draft.EntitlementId, draft.FulfillmentId, draft.PacsRef,
                draft.PersonRef, draft.VendorAccessLevelRef, draft.PacsCardNumber, draft.Details, now);
            db.ReconciliationFindings.Add(finding);

            if (draft.Kind is FindingKind.MissingInPacs or FindingKind.Mismatch
                && draft.FulfillmentId is { } fid
                && fulfillments.TryGetValue(fid, out var fulfillment)
                && fulfillment.Status == FulfillmentStatus.Confirmed)
            {
                fulfillment.DetectDrift(finding.Id.Value, now);
            }

            var key = (draft.Kind, draft.PersonRef, draft.VendorAccessLevelRef);
            if (!openTasks.TryGetValue(key, out var taskId))
            {
                var siteId = draft.AreaId is { } areaId && siteByArea.TryGetValue(areaId, out var site) ? site : fallbackSite;
                await tasks.OpenAsync(StaffTaskKind.ReconciliationFinding, siteId, TaskSubjectType, finding.Id.Value, Actor, now, ct);
                taskId = db.StaffTasks.Local.Single(t => t.Kind == StaffTaskKind.ReconciliationFinding
                    && t.SubjectId == finding.Id.Value).Id;
                openTasks[key] = taskId;
            }

            finding.AttachTask(taskId);
            outbox.Enqueue(new ReconciliationFindingCreated(finding.Id, report.Id, finding.Kind, taskId, now), CommandPriority.Normal);
        }
    }

    // Unresolved ReconciliationFinding tasks of earlier findings of this instance, by (Kind, PersonRef, VendorAccessLevelRef).
    private async Task<Dictionary<(FindingKind, string?, string), StaffTaskId>> OpenTasksByKeyAsync(
        PacsInstanceId instanceId, CancellationToken ct)
    {
        var open = await (from f in db.ReconciliationFindings.AsNoTracking()
                          join r in db.ReconciliationReports.AsNoTracking() on f.ReportId equals r.Id
                          join t in db.StaffTasks.AsNoTracking() on f.StaffTaskId equals (StaffTaskId?)t.Id
                          where r.PacsInstanceId == instanceId
                              && t.Kind == StaffTaskKind.ReconciliationFinding
                              && t.Status != StaffTaskStatus.Resolved
                          orderby f.CreatedAt
                          select new { f.Kind, f.PersonRef, f.VendorAccessLevelRef, TaskId = t.Id })
            .ToListAsync(ct);
        var byKey = new Dictionary<(FindingKind, string?, string), StaffTaskId>();
        foreach (var o in open)
        {
            byKey.TryAdd((o.Kind, o.PersonRef, o.VendorAccessLevelRef), o.TaskId);
        }

        return byKey;
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Reconciliation {ReportId} completed: {ExportedCount} exported, {ComparedCount} compared, {InSyncCount} in "
            + "sync, {FindingCount} findings")]
    private partial void LogCompleted(Guid reportId, int exportedCount, int comparedCount, int inSyncCount, int findingCount);
}
