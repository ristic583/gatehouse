using Gatehouse.Connector.Contracts.Events;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.Application.Connectors.Messages;

public sealed record ConnectorCommandAcked(
    string TenantId,
    PacsInstanceId PacsInstanceId,
    ConnectorId ConnectorId,
    DateTimeOffset ReceivedAt,
    CommandId CommandId,
    FulfillmentId? FulfillmentId,
    AckStatus Status,
    string? PacsRef,
    AckError? Error,
    bool IsFinal);
