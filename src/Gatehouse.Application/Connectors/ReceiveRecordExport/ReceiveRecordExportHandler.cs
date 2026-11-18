using Gatehouse.Application.Common.Messaging;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Connectors.Messages;
using Gatehouse.Connector.Contracts.Events;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.Reconciliation;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Connectors.ReceiveRecordExport;

/// <summary>
/// <c>POST /connector/v1/exports</c> (connector-protocol.md §2.6): accepts chunks in order, stages their records and, after
/// the last chunk, publishes <see cref="RecordExportReceived"/> through the tenant outbox. Card numbers are never logged.
/// </summary>
public sealed class ReceiveRecordExportHandler(ITenantDb db, ConnectorContext connector, IOutboxWriter outbox, TimeProvider time)
{
    public const int MaxRecords = 1000;

    public async Task<Result<RecordExportResponse>> HandleAsync(RecordExport export, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(export);
        var records = export.Records ?? [];
        if (records.Length > MaxRecords)
        {
            return Error.Validation("connector.payload_too_large", $"At most {MaxRecords} records per chunk.");
        }

        if (export.ChunkIndex < 0)
        {
            return Error.Validation("connector.validation_failed", "chunkIndex must not be negative.");
        }

        var reportId = new ReconciliationReportId(export.ExportId);
        var instanceId = connector.PacsInstanceId;
        var report = await db.ReconciliationReports.FirstOrDefaultAsync(r => r.Id == reportId && r.PacsInstanceId == instanceId, ct);
        if (report is null)
        {
            return Error.NotFound("connector.export_not_found", "Unknown export for this PACS instance.");
        }

        var accepted = report.AcceptChunk(export.ChunkIndex, export.CapturedAt, export.IsLast);
        if (accepted.IsFailure)
        {
            return Error.Conflict("connector.export_closed", "The export's reconciliation is already closed.");
        }

        switch (accepted.Value)
        {
            case ChunkAcceptance.Duplicate:
                return new RecordExportResponse(false, true);
            case ChunkAcceptance.OutOfOrder:
                return OutOfOrder();
        }

        for (var i = 0; i < records.Length; i++)
        {
            var r = records[i];
            db.ReconciliationExportRecords.Add(new ReconciliationExportRecord(
                reportId, export.ChunkIndex, i, r.RecordType.ToString(), r.PacsRef, r.PersonRef, r.CardNumber, r.VendorAccessLevelRef,
                r.ValidFrom, r.ValidTo, r.IsActive));
        }

        if (export.IsLast)
        {
            var staged = await db.ReconciliationExportRecords.CountAsync(e => e.ReportId == reportId, ct);
            outbox.Enqueue(
                new RecordExportReceived(connector.TenantId.Value, instanceId, connector.ConnectorId, time.GetUtcNow(), export.ExportId,
                    report.ReceivedChunkCount, staged + records.Length, report.CapturedAt ?? export.CapturedAt),
                CommandPriority.Normal);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // A concurrent request accepted the same chunk index first (xmin conflict or duplicate primary key).
            return OutOfOrder();
        }

        return new RecordExportResponse(true, false);
    }

    private static Error OutOfOrder() =>
        Error.Conflict("connector.export_chunk_out_of_order", "Export chunks must arrive in order; restart from chunk 0.");
}
