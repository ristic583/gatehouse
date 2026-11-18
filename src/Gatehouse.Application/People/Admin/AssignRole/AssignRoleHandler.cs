using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.People.Dtos;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.People.Admin.AssignRole;

/// <summary>
/// <c>POST /api/v1/admin/role-assignments</c> (<c>authorization.md</c> §2, §11 rule 5): active Employees only, <c>TenantAdmin</c>
/// never site-scoped. Approval rights stay derived; there is no approver role. Audit <c>role.assigned</c>.
/// </summary>
public sealed class AssignRoleHandler(ITenantDb db, PeopleAdminAudit audit)
{
    public async Task<Result<RoleAssignmentDto>> HandleAsync(AssignRoleCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var request = command.Request;
        ArgumentNullException.ThrowIfNull(request);
        var personId = new PersonId(request.PersonId);
        var person = await db.Persons.AsNoTracking().SingleOrDefaultAsync(p => p.Id == personId, ct);
        if (person is null)
        {
            return Error.NotFound("person.not_found", "The person does not exist.");
        }

        if (person.Type != PersonType.Employee || person.Status != PersonStatus.Active)
        {
            return Error.Validation("role.person_not_employee", "Roles can only be assigned to active employees.");
        }

        SiteId? siteId = request.SiteId is { } s ? new SiteId(s) : null;
        string? siteName = null;
        if (siteId is { } sid)
        {
            siteName = await db.Sites.AsNoTracking().Where(x => x.Id == sid).Select(x => x.Name).SingleOrDefaultAsync(ct);
            if (siteName is null)
            {
                return Error.NotFound("site.not_found", "The site does not exist.");
            }
        }

        var assignment = RoleAssignment.Create(personId, request.Role, siteId);
        if (assignment.IsFailure)
        {
            return assignment.Error;
        }

        if (await db.RoleAssignments.AnyAsync(r => r.PersonId == personId && r.Role == request.Role && r.SiteId == siteId, ct))
        {
            return Error.Conflict("role.assignment_exists", "The person already has this role.");
        }

        db.RoleAssignments.Add(assignment.Value);
        var payload = new { role = request.Role.ToString(), siteId = request.SiteId };
        await audit.RecordAsync("role.assigned", "Person", personId.Value.ToString(), payload, ct);
        await db.SaveChangesAsync(ct);
        var personRef = new PersonRefDto(person.Id.Value, person.GivenName + " " + person.FamilyName);
        return new RoleAssignmentDto(assignment.Value.Id.Value, personRef, request.Role, request.SiteId, siteName);
    }
}
