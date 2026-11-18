using Gatehouse.Domain.Common;
using Gatehouse.Domain.Sites;
using Gatehouse.Infrastructure.Persistence.Tenant;

namespace Gatehouse.IntegrationTests.Persistence;

internal static class LiveBuildingMappingData
{
    public static Door Door(TenantDbContext db)
    {
        var (_, floor, level) = MappingData.Building(db);
        var owner = MappingData.Employee($"owner-{Guid.NewGuid():N}@example.test");
        var area = Area.Create(floor.Id, "Server room", "SRV-" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(), MappingData.Rect(10, 10, 100, 100), level.Id, owner.Id, owner.Type).Value;
        var door = Domain.Sites.Door.Create(area.Id, "Server door", new Point(50, 100), "D-SRV").Value;
        db.AddRange(owner, area, door);
        return door;
    }
}
