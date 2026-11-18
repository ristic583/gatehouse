using Gatehouse.Domain.LiveBuilding;
using Gatehouse.Domain.People;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.IntegrationTests.Persistence;

public sealed class DoorCommandMappingTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Round_trips()
    {
        var ct = TestContext.Current.CancellationToken;
        var testDb = await TenantTestDb.CreateAsync(postgres, ct);
        var command = DoorCommand.Request(DoorCommandKind.MomentaryUnlock, DoorCommandScope.Door, Guid.NewGuid(), PersonId.New(), "Delivery", 15, 1, MappingData.Now).Value;
        await using (var db = testDb.NewContext())
        {
            db.DoorCommands.Add(command);
            await db.SaveChangesAsync(ct);
        }

        await using var read = testDb.NewContext();
        var stored = await read.DoorCommands.AsNoTracking().SingleAsync(c => c.Id == command.Id, ct);
        Assert.Equal(DoorCommandKind.MomentaryUnlock, stored.Kind);
        Assert.Equal(DoorCommandScope.Door, stored.Scope);
        Assert.Equal(15, stored.DurationSeconds);
        Assert.Equal("Delivery", stored.Reason);
        Assert.Equal(command.RequestedById, stored.RequestedById);
    }
}
