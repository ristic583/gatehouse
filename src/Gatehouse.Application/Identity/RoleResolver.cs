using Gatehouse.Application.Common.Persistence;
using Gatehouse.Domain.People;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Identity;

/// <summary>Derived (from data) plus assigned role names of a person (authorization.md §2).</summary>
public sealed class RoleResolver(ITenantDb db)
{
    public async Task<IReadOnlyList<string>> GetRolesAsync(PersonId personId, CancellationToken ct)
    {
        var person = await db.Persons.AsNoTracking()
            .Where(p => p.Id == personId)
            .Select(p => new { p.Type })
            .SingleOrDefaultAsync(ct);
        if (person is null)
        {
            return [];
        }

        var roles = new List<string> { person.Type.ToString() };
        if (await db.Persons.AnyAsync(p => p.ManagerId == personId && p.Status == PersonStatus.Active, ct))
        {
            roles.Add("Manager");
        }

        if (await db.Persons.AnyAsync(p => p.SponsorId == personId && p.Status == PersonStatus.Active, ct))
        {
            roles.Add("Sponsor");
        }

        if (await db.Areas.AnyAsync(a => a.Owners.Any(o => o.PersonId == personId), ct))
        {
            roles.Add("AreaOwner");
        }

        var assigned = await db.RoleAssignments.AsNoTracking()
            .Where(r => r.PersonId == personId)
            .Select(r => r.Role)
            .Distinct()
            .ToListAsync(ct);
        roles.AddRange(assigned.OrderBy(r => r).Select(r => r.ToString()));
        return roles;
    }
}
