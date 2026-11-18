using Gatehouse.Domain.Access;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;
using Gatehouse.Infrastructure.Persistence.Tenant;

namespace Gatehouse.IntegrationTests.Persistence;

internal static class AccessData
{
    public static (Entitlement Entitlement, Person Manager, Person Owner, Area Area) Seed(TenantDbContext db)
    {
        var (_, floor, level) = MappingData.Building(db);
        var subject = MappingData.Employee($"s-{Guid.NewGuid():N}@x.test");
        var manager = MappingData.Employee($"m-{Guid.NewGuid():N}@x.test");
        var owner = MappingData.Employee($"o-{Guid.NewGuid():N}@x.test");
        var area = Area.Create(floor.Id, "Server", "A-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(), MappingData.Rect(0, 0, 10, 10), level.Id, owner.Id, PersonType.Employee).Value;
        var entitlement = Entitlement.Create(
            subject.Id,
            area.Id,
            subject.Id,
            MappingData.Now.AddDays(1),
            MappingData.Now.AddDays(30),
            MappingData.Now.AddDays(30),
            "Needed for the migration project",
            [new StepPlan(ApproverKind.Manager, manager.Id), new StepPlan(ApproverKind.AreaOwner, null)],
            MappingData.Now);
        db.AddRange(subject, manager, owner, area, entitlement);
        return (entitlement, manager, owner, area);
    }
}
