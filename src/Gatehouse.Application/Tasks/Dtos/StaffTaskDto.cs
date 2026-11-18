using Gatehouse.Application.People.Dtos;

namespace Gatehouse.Application.Tasks.Dtos;

public sealed record StaffTaskDto(
    Guid TaskId,
    string Kind,
    string Status,
    Guid? SiteId,
    string? SiteName,
    string SubjectType,
    Guid SubjectId,
    string Summary,
    PersonRefDto? AssignedTo,
    string? ResolutionAction,
    string? ResolutionNote,
    PersonRefDto? ResolvedBy,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset CreatedAt,
    string? FindingKind = null,
    string? PersonName = null);
