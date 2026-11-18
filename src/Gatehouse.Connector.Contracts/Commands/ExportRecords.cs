using Gatehouse.Connector.Contracts.Adapters;

namespace Gatehouse.Connector.Contracts.Commands;

public sealed record ExportRecords(Guid ExportId, RecordType[] RecordTypes, string[]? VendorAccessLevelRefs);
