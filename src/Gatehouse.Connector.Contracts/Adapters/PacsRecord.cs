using Gatehouse.Connector.Contracts.Events;

namespace Gatehouse.Connector.Contracts.Adapters;

public sealed record PacsRecord(
    RecordType RecordType, string PacsRef, string? PersonRef, string? CardNumber, string? VendorAccessLevelRef, DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo, bool IsActive);
