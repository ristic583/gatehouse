using Gatehouse.Connector.Contracts.Adapters;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.Application.Connectors.Messages;

public sealed record ConnectorCapabilitiesReported(
    string TenantId,
    PacsInstanceId PacsInstanceId,
    ConnectorId ConnectorId,
    DateTimeOffset ReceivedAt,
    PacsCapabilities Capabilities);
