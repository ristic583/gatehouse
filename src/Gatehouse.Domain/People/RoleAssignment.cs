using Gatehouse.Domain.Common;
using Gatehouse.Domain.Sites;

namespace Gatehouse.Domain.People;

public sealed class RoleAssignment : Entity<RoleAssignmentId>
{
    private RoleAssignment(RoleAssignmentId id, PersonId personId, AssignedRole role, SiteId? siteId)
        : base(id)
    {
        PersonId = personId;
        Role = role;
        SiteId = siteId;
    }

    public PersonId PersonId { get; private set; }

    public AssignedRole Role { get; private set; }

    public SiteId? SiteId { get; private set; }

    public static Result<RoleAssignment> Create(PersonId personId, AssignedRole role, SiteId? siteId)
    {
        if (role == AssignedRole.TenantAdmin && siteId is not null)
        {
            return Error.Validation("role.tenant_admin_cannot_be_site_scoped", "Tenant admin cannot be scoped to a site.");
        }

        return new RoleAssignment(RoleAssignmentId.New(), personId, role, siteId);
    }
}
