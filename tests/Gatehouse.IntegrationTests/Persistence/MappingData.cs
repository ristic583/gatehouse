using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;
using Gatehouse.Infrastructure.Persistence.Tenant;

namespace Gatehouse.IntegrationTests.Persistence;

internal static class MappingData
{
    public static readonly DateTimeOffset Now = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

    public static Person Employee(string email) =>
        Person.Create(
            PersonSource.Authoritative,
            PersonType.Employee,
            "Erik",
            "Moller",
            email,
            Eligibility.Create(Now.AddYears(-1), null, "Europe/Copenhagen").Value,
            null,
            null,
            Now).Value;

    public static (Site Site, Floor Floor, SecurityLevel Level) Building(TenantDbContext db)
    {
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var site = Site.Create("Site " + suffix, "S-" + suffix, "Europe/Copenhagen").Value;
        var floor = Floor.Create(site.Id, "Ground", 0, 1000, 600).Value;
        var level = SecurityLevel.Create("Level " + suffix, Random.Shared.Next(1, 1_000_000), ApproverChain.Both, 182).Value;
        db.AddRange(site, floor, level);
        return (site, floor, level);
    }

    public static Polygon Rect(double x1, double y1, double x2, double y2) =>
        Polygon.Create([new(x1, y1), new(x2, y1), new(x2, y2), new(x1, y2)], 1000, 600).Value;
}
