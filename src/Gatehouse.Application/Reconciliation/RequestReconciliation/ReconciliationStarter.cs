using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Connectors;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Reconciliation;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Reconciliation.RequestReconciliation;

/// <summary>
/// Shared request logic (staff request and scheduler): checks the instance can be reconciled, then adds the report, its
/// <c>ExportRecords</c> command and the audit entry to the unit of work. The caller saves and signals the connector.
/// </summary>
public sealed class ReconciliationStarter(ITenantDb db, ConnectorCommandFactory factory, IAuditLog audit, TimeProvider time)
{
    public const string AuditSubject = "ReconciliationReport";

    public async Task<Result<ReconciliationReport>> StartAsync(
        PacsInstance instance, PersonId? requestedById, AuditActor actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (instance.FulfillmentMode != FulfillmentMode.Connector)
        {
            return Error.Conflict("reconciliation.connector_required", "Only connector-managed PACS instances can be reconciled.");
        }

        if (PacsCapabilityGaps.From(instance.CapabilitiesJson) is { } gaps && gaps.Contains(PacsCapabilityGaps.RecordExport))
        {
            return Error.Conflict("reconciliation.export_unsupported", "This PACS does not support record export.");
        }

        var running = await db.ReconciliationReports.AnyAsync(
            r => r.PacsInstanceId == instance.Id
                && (
                    r.Status == ReconciliationStatus.Requested || r.Status == ReconciliationStatus.Exporting
                        || r.Status == ReconciliationStatus.Comparing),
            ct);
        if (running)
        {
            return Error.Conflict("reconciliation.already_running", "A reconciliation of this PACS instance is already running.");
        }

        var levelRefs = (await db.AccessLevelMappings.AsNoTracking().Where(m => m.PacsInstanceId == instance.Id)
                .Select(m => m.VendorAccessLevelRef).Distinct().ToListAsync(ct))
            .Order(StringComparer.Ordinal).ToList();
        var reportId = ReconciliationReportId.New();
        var command = await factory.CreateForExportAsync(instance.Id, reportId, levelRefs, ct);
        var report = ReconciliationReport.Request(reportId, instance.Id, requestedById, command.Id, time.GetUtcNow());
        db.ConnectorCommands.Add(command);
        db.ReconciliationReports.Add(report);
        audit.Record(actor, "reconciliation.requested", AuditSubject, report.Id.Value.ToString(), new
        {
            pacsInstanceId = instance.Id.Value,
            commandId = command.Id.Value,
        });
        return report;
    }
}
