using Gatehouse.Domain.LiveBuilding;
using Gatehouse.Domain.People;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.IntegrationTests.Persistence;

public sealed class AlarmMappingTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Only_one_open_alarm_per_door_and_kind()
    {
        var ct = TestContext.Current.CancellationToken;
        var testDb = await TenantTestDb.CreateAsync(postgres, ct);
        var now = MappingData.Now;
        Domain.Sites.Door door;
        await using (var db = testDb.NewContext())
        {
            door = LiveBuildingMappingData.Door(db);
            var acknowledged = Alarm.Raise(door.Id, AlarmKind.Forced, Guid.NewGuid(), now);
            acknowledged.Acknowledge(PersonId.New(), "Checked", now.AddMinutes(1));
            db.Alarms.AddRange(acknowledged, Alarm.Raise(door.Id, AlarmKind.Forced, Guid.NewGuid(), now.AddMinutes(2)), Alarm.Raise(door.Id, AlarmKind.HeldOpen, Guid.NewGuid(), now));
            await db.SaveChangesAsync(ct);
        }

        await using (var read = testDb.NewContext())
        {
            var open = await read.Alarms.AsNoTracking().Where(a => a.AcknowledgedAt == null).ToListAsync(ct);
            Assert.Equal(2, open.Count);
            Assert.Equal("Checked", (await read.Alarms.AsNoTracking().SingleAsync(a => a.AcknowledgedAt != null, ct)).Comment);
        }

        await using var again = testDb.NewContext();
        again.Alarms.Add(Alarm.Raise(door.Id, AlarmKind.Forced, Guid.NewGuid(), now.AddMinutes(3)));
        await Assert.ThrowsAsync<DbUpdateException>(() => again.SaveChangesAsync(ct));
    }
}
