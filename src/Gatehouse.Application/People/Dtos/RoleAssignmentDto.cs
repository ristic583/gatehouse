using Gatehouse.Domain.People;

namespace Gatehouse.Application.People.Dtos;

public sealed record RoleAssignmentDto(Guid Id, PersonRefDto Person, AssignedRole Role, Guid? SiteId, string? SiteName);
