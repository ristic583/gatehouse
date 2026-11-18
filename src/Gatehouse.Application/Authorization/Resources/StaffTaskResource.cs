using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;
using Gatehouse.Domain.Tasks;

namespace Gatehouse.Application.Authorization.Resources;

public sealed record StaffTaskResource(
    StaffTaskId TaskId, StaffTaskKind Kind, SiteId? SiteId, PersonId? AssignedToId, StaffTaskStatus Status);
