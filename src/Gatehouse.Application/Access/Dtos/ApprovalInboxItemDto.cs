using Gatehouse.Application.People.Dtos;

namespace Gatehouse.Application.Access.Dtos;

public sealed record ApprovalInboxItemDto(
    Guid EntitlementId,
    Guid StepId,
    int StepOrder,
    string ApproverKind,
    PersonRefDto Subject,
    PersonRefDto RequestedBy,
    AreaRefDto Area,
    string SecurityLevelName,
    DateTimeOffset RequestedStart,
    DateTimeOffset RequestedEnd,
    DateTimeOffset EffectiveEnd,
    bool CappedToEligibility,
    string Justification,
    DateTimeOffset CreatedAt);
