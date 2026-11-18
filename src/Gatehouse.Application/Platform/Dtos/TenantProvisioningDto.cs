using Gatehouse.Domain.Platform;

namespace Gatehouse.Application.Platform.Dtos;

public sealed record TenantProvisioningDto(
    ProvisioningStep Step,
    ProvisioningState State,
    int Attempts,
    string? LastError,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? AdminInvitedAt,
    DateTimeOffset? AdminActivatedAt);
