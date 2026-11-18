using Gatehouse.Domain.People;

namespace Gatehouse.Application.People.Dtos;

public sealed record AssignRoleRequest(Guid PersonId, AssignedRole Role, Guid? SiteId);
