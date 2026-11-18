using Gatehouse.Connector.Contracts.Events;

namespace Gatehouse.Connector.Contracts.Adapters;

public sealed record ExportScope(IReadOnlyList<RecordType> RecordTypes, IReadOnlyList<string>? VendorAccessLevelRefs);
