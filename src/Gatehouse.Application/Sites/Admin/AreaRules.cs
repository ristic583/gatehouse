using Gatehouse.Application.Common.Persistence;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Sites.Admin;

/// <summary>Owner and floor rules shared by the area admin handlers (<c>domain-model.md</c> §2.1, §2.10).</summary>
public static class AreaRules
{
    /// <summary>Owners: ≥ 1, each an active Employee of this tenant (<c>area.owner_not_employee</c> otherwise).</summary>
    public static async Task<Result<List<(PersonId PersonId, PersonType Type)>>> OwnersAsync(
        ITenantDb db, IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return Error.Conflict("area.last_owner", "An area must keep at least one owner.");
        }

        var personIds = ids.Distinct().Select(i => new PersonId(i)).ToList();
        var people = await db.Persons.AsNoTracking().Where(p => personIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Type, p.Status }).ToListAsync(ct);
        if (people.Count != personIds.Count || people.Any(p => p.Type != PersonType.Employee || p.Status != PersonStatus.Active))
        {
            return Error.Validation("area.owner_not_employee", "Every area owner must be an active employee.");
        }

        return people.Select(p => (p.Id, p.Type)).ToList();
    }

    /// <summary>Security level exists; name unique per floor; code unique per tenant; overlap ≤ 1 % with siblings.</summary>
    public static async Task<Result> CheckAsync(ITenantDb db, Area area, AreaId? except, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(area);
        var levelId = area.SecurityLevelId;
        if (!await db.SecurityLevels.AnyAsync(l => l.Id == levelId, ct))
        {
            return Error.NotFound("security_level.not_found", "The security level does not exist.");
        }

        var floorId = area.FloorId;
        var siblings = await db.Areas.AsNoTracking().Where(a => a.FloorId == floorId && a.Id != except).ToListAsync(ct);
        if (siblings.Any(a => string.Equals(a.Name, area.Name, StringComparison.Ordinal)))
        {
            return Error.Conflict("area.name_taken", "Another area on this floor has this name.");
        }

        var code = area.Code;
        if (await db.Areas.AnyAsync(a => a.Code == code && a.Id != except, ct))
        {
            return Error.Conflict("area.code_taken", "Another area has this code.");
        }

        if (siblings.Any(a => PolygonOverlap.Fraction(a.Polygon, area.Polygon) > PolygonOverlap.MaxFraction))
        {
            return Error.Validation("area.overlaps", "The area overlaps another area on this floor by more than 1 %.");
        }

        return Result.Success();
    }
}
