using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.People.Dtos;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.People.Admin.ListRoleAssignments;

/// <summary><c>GET /api/v1/admin/role-assignments</c>, ordered by role and name.</summary>
public sealed class ListRoleAssignmentsHandler(ITenantDb db)
{
    public async Task<Result<IReadOnlyList<RoleAssignmentDto>>> HandleAsync(ListRoleAssignmentsQuery query, CancellationToken ct)
    {
        var rows = await (from r in db.RoleAssignments.AsNoTracking()
                          join p in db.Persons.AsNoTracking() on r.PersonId equals p.Id
                          join s in db.Sites.AsNoTracking() on r.SiteId equals (SiteId?)s.Id into sites
                          from s in sites.DefaultIfEmpty()
                          orderby r.Role, p.FamilyName, p.GivenName
                          select new
                          {
                              r.Id,
                              PersonId = p.Id,
                              p.GivenName,
                              p.FamilyName,
                              r.Role,
                              r.SiteId,
                              SiteName = s == null ? null : s.Name,
                          })
            .ToListAsync(ct);
        IReadOnlyList<RoleAssignmentDto> dtos = rows
            .Select(r => new RoleAssignmentDto(
                r.Id.Value, new PersonRefDto(r.PersonId.Value, r.GivenName + " " + r.FamilyName), r.Role, r.SiteId?.Value, r.SiteName))
            .ToList();
        return Result.Success(dtos);
    }
}
