using Gatehouse.Connector.Contracts.Events;

namespace Gatehouse.Connector.Contracts.Adapters;

public sealed record DoorCommand(
    DoorCommandKind Kind, IReadOnlyList<string> PacsDoorRefs, int? DurationSeconds, LockdownAction? LockdownAction, string Reason);
