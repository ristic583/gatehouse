using Gatehouse.Application.People.Dtos;

namespace Gatehouse.Application.Access.Dtos;

public sealed record ApprovalStepDto(
    Guid StepId,
    int Order,
    string ApproverKind,
    string Status,
    PersonRefDto? AssignedApprover,
    PersonRefDto? DecidedBy,
    DateTimeOffset? DecidedAt,
    string? Comment);
