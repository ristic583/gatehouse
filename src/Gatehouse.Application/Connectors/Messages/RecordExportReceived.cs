using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.Application.Connectors.Messages;

/// <summary>A complete export was staged in <c>reconciliation_export_records</c>; <see cref="ExportId"/> is the report id.</summary>
public sealed record RecordExportReceived(
    string TenantId,
    PacsInstanceId PacsInstanceId,
    ConnectorId ConnectorId,
    DateTimeOffset ReceivedAt,
    Guid ExportId,
    int ChunkCount,
    int RecordCount,
    DateTimeOffset CapturedAt);
