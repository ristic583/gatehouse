using Gatehouse.Connector.Contracts.Events;

namespace Gatehouse.Connector.Contracts.Protocol;

public sealed record DoorEventsRequest(IReadOnlyList<DoorEvent> Events);
