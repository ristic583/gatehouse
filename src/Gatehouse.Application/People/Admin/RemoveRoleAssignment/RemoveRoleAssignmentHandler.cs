using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Identity;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.People.Admin.RemoveRoleAssignment;

/// <summary>
/// <c>DELETE /api/v1/admin/role-assignments/{roleAssignmentId}</c> (<c>authorization.md</c> §11 rule 5): the last
/// <c>TenantAdmin</c> cannot be removed; audit <c>role.removed</c>.
/// </summary>
public sealed class RemoveRoleAssignmentHandler(ITenantDb db, PeopleAdminAudit audit, ICurrentUser user)
{
    public async Task<Result> HandleAsync(RemoveRoleAssignmentCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var id = new RoleAssignmentId(command.RoleAssignmentId);
        var assignment = await db.RoleAssignments.SingleOrDefaultAsync(r => r.Id == id, ct);
        if (assignment is null)
        {
            return Error.NotFound("role.assignment_not_found", "The role assignment does not exist.");
        }

        if (assignment.Role == AssignedRole.TenantAdmin)
        {
            // Covers removing one's own TenantAdmin too: allowed only while another tenant admin remains.
            var others = await db.RoleAssignments
                .CountAsync(r => r.Role == AssignedRole.TenantAdmin && r.PersonId != assignment.PersonId, ct);
            if (others == 0)
            {
                return Error.Conflict("role.last_tenant_admin", "The last tenant admin cannot be removed.");
            }
        }

        db.RoleAssignments.Remove(assignment);
        await audit.RecordAsync("role.removed", "Person", assignment.PersonId.Value.ToString(),
            new { role = assignment.Role.ToString(), siteId = assignment.SiteId?.Value, self = assignment.PersonId == user.PersonId }, ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (LastTenantAdminException)
        {
            return Error.Conflict("role.last_tenant_admin", "The last tenant admin cannot be removed.");
        }

        return Result.Success();
    }
}
