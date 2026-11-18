using Gatehouse.Connector.Contracts.Adapters;

namespace Gatehouse.Connector.Contracts.Events;

public sealed record RecordExport(
    Guid ExportId, Guid CommandId, int ChunkIndex, bool IsLast, DateTimeOffset CapturedAt, PacsRecord[] Records);
