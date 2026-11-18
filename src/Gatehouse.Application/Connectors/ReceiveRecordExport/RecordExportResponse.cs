namespace Gatehouse.Application.Connectors.ReceiveRecordExport;

/// <summary>Response of <c>POST /connector/v1/exports</c> (202).</summary>
public sealed record RecordExportResponse(bool Accepted, bool Duplicate);
