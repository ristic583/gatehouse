using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.IntegrationTests.Persistence;

public sealed class SitesMappingTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Area_with_polygon_and_owners_round_trips()
    {
        var ct = TestContext.Current.CancellationToken;
        var testDb = await TenantTestDb.CreateAsync(postgres, ct);
        var owner1 = MappingData.Employee("o1@x.test");
        var owner2 = MappingData.Employee("o2@x.test");
        AreaId areaId;

        await using (var db = testDb.NewContext())
        {
            var (_, floor, level) = MappingData.Building(db);
            db.Persons.AddRange(owner1, owner2);
            var area = Area.Create(floor.Id, "Server Room", "CPH-SERVER", MappingData.Rect(540, 40, 960, 300), level.Id, owner1.Id, PersonType.Employee).Value;
            area.AddOwner(owner2.Id, PersonType.Employee);
            var door = Door.Create(area.Id, "Server Room door", new(750, 300), "D-CPH-SERVER").Value;
            db.Areas.Add(area);
            db.Doors.Add(door);
            db.Readers.Add(Reader.Create(door.Id, "R-CPH-SERVER", ReaderDirection.In, new(750, 288), null).Value);
            await db.SaveChangesAsync(ct);
            areaId = area.Id;
        }

        await using (var db = testDb.NewContext())
        {
            var area = await db.Areas.SingleAsync(a => a.Id == areaId, ct);
            Assert.Equal(MappingData.Rect(540, 40, 960, 300), area.Polygon);
            Assert.Equal(2, area.Owners.Count);
            Assert.NotEqual(0u, area.Version);
            var door = await db.Doors.SingleAsync(d => d.AreaId == areaId, ct);
            Assert.Equal(new Gatehouse.Domain.Common.Point(750, 300), door.Position);
            Assert.Equal(1, await db.Readers.CountAsync(r => r.DoorId == door.Id, ct));
        }
    }

    [Fact]
    public async Task Duplicate_area_code_violates_unique_index()
    {
        var ct = TestContext.Current.CancellationToken;
        var testDb = await TenantTestDb.CreateAsync(postgres, ct);
        await using var db = testDb.NewContext();
        var (_, floor, level) = MappingData.Building(db);
        var owner = MappingData.Employee("o@x.test");
        db.Persons.Add(owner);
        db.Areas.Add(Area.Create(floor.Id, "A", "DUP-CODE", MappingData.Rect(0, 0, 10, 10), level.Id, owner.Id, PersonType.Employee).Value);
        db.Areas.Add(Area.Create(floor.Id, "B", "DUP-CODE", MappingData.Rect(20, 20, 30, 30), level.Id, owner.Id, PersonType.Employee).Value);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
    }
}
