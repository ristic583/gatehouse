using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.Application.LiveBuilding.Dtos;

/// <summary>Health of one PACS instance; Manual instances are always online with null agent fields.</summary>
public sealed record ConnectorHealthDto(
    Guid PacsInstanceId,
    string Name,
    string Vendor,
    FulfillmentMode FulfillmentMode,
    bool Online,
    DateTimeOffset? LastHeartbeatAt,
    string? AgentVersion,
    int? AgentQueueDepth,
    int QueuedCommands,
    long? LastAckLatencyMs,
    bool? PacsReachable,
    IReadOnlyList<string>? CapabilityGaps = null);
