using Gatehouse.Connector.Contracts.Events;

namespace Gatehouse.Connector.Contracts.Adapters;

public sealed record PacsCapabilities(
    string Vendor, string AdapterVersion, bool NativeValidity, bool PinWrite, bool PhotoWrite, bool PushEvents, bool RecordExport,
    IReadOnlyList<DoorCommandKind> DoorCommands, int MaxBatchSize);
