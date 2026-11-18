using Gatehouse.Application.Connectors.Messages;
using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.Application.LiveBuilding.IngestDoorEvents;

public sealed record IngestDoorEventsCommand(
    PacsInstanceId PacsInstanceId, DateTimeOffset ReceivedAt, IReadOnlyList<NormalizedDoorEvent> Events);
