using Gatehouse.Application.Audit.Dtos;

namespace Gatehouse.Application.Fulfillment.Dtos;

public sealed record FulfillmentDetailDto(
    FulfillmentQueueItemDto Item,
    DesiredStateDto Desired,
    ConfirmedStateDto? Confirmed,
    IReadOnlyList<AuditEntryDto> History,
    bool ConnectorManaged = false,
    string? CommandStatus = null,
    int? CommandAttempts = null,
    DateTimeOffset? NextAttemptAt = null,
    string? LastError = null,
    IReadOnlyList<string>? CapabilityGaps = null);
