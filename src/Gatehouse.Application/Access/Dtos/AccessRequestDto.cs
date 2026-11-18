using Gatehouse.Application.People.Dtos;

namespace Gatehouse.Application.Access.Dtos;

public sealed record AccessRequestDto(
    Guid EntitlementId,
    PersonRefDto Subject,
    PersonRefDto RequestedBy,
    AreaRefDto Area,
    DateTimeOffset RequestedStart,
    DateTimeOffset RequestedEnd,
    DateTimeOffset EffectiveEnd,
    bool CappedToEligibility,
    string Justification,
    string Status,
    IReadOnlyList<ApprovalStepDto> Steps,
    string? FulfillmentStatus,
    DateTimeOffset CreatedAt,
    RevocationDto? Revocation = null,
    string? RevocationFulfillmentStatus = null);
