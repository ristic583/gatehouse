using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Connectors.Messages;
using Gatehouse.Application.Reconciliation.RequestReconciliation;
using Gatehouse.Connector.Contracts.Events;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Reconciliation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Gatehouse.Application.Reconciliation.FailReconciliation;

/// <summary>Fails a running report (audit <c>reconciliation.failed</c>) and drops its staged rows; the consumer saves once.</summary>
public sealed partial class FailReconciliationHandler(
    ITenantDb db, IAuditLog audit, TimeProvider time, ILogger<FailReconciliationHandler> logger)
{
    private static readonly AuditActor Actor = AuditActor.ForSystem("reconciliation");

    public async Task<Result> HandleAsync(ReconciliationReportId reportId, string code, string message, CancellationToken ct)
    {
        var report = await db.ReconciliationReports.FirstOrDefaultAsync(r => r.Id == reportId, ct);
        return report is null ? Result.Success() : await FailAsync(report, code, message, ct);
    }

    /// <summary>A final, not <c>Applied</c> ack of a report's <c>ExportRecords</c> command fails the report; other acks are
    /// ignored.</summary>
    public async Task<Result> HandleAckAsync(ConnectorCommandAcked ack, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ack);
        if (!ack.IsFinal || ack.Status == AckStatus.Applied)
        {
            return Result.Success();
        }

        var report = await db.ReconciliationReports.FirstOrDefaultAsync(r => r.CommandId == ack.CommandId
            && r.PacsInstanceId == ack.PacsInstanceId, ct);
        return report is null
            ? Result.Success()
            : await FailAsync(
                report, ack.Error?.Code ?? "connector.failed", ack.Error?.Message ?? "The connector could not export the records.", ct);
    }

    private async Task<Result> FailAsync(ReconciliationReport report, string code, string message, CancellationToken ct)
    {
        if (!report.IsRunning)
        {
            return Result.Success();
        }

        var failed = report.Fail(code, message, time.GetUtcNow());
        if (failed.IsFailure)
        {
            return failed;
        }

        audit.Record(Actor, "reconciliation.failed", ReconciliationStarter.AuditSubject, report.Id.Value.ToString(), new
        {
            pacsInstanceId = report.PacsInstanceId.Value,
            code,
        });
        db.ReconciliationExportRecords.RemoveRange(
            await db.ReconciliationExportRecords.Where(r => r.ReportId == report.Id).ToListAsync(ct));
        LogFailed(report.Id.Value, code);
        return Result.Success();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reconciliation {ReportId} failed with {Code}")]
    private partial void LogFailed(Guid reportId, string code);
}
