using Gatehouse.Application.LiveBuilding.Dtos;
using Gatehouse.Domain.LiveBuilding;
using Gatehouse.Domain.Sites;
using Gatehouse.Infrastructure.LiveBuilding;

namespace Gatehouse.UnitTests.LiveBuilding;

public sealed class InMemoryDoorStateStoreTests
{
    [Fact]
    public async Task Keys_are_tenant_scoped()
    {
        var ct = TestContext.Current.CancellationToken;
        var cache = new InMemoryDoorStateCache();
        var nordlys = new InMemoryDoorStateStore(cache, new FixedTenantContext("nordlys"));
        var other = new InMemoryDoorStateStore(cache, new FixedTenantContext("aurora"));
        var door = DoorId.New();
        var missing = DoorId.New();
        var at = new DateTimeOffset(2026, 11, 10, 8, 0, 0, TimeSpan.Zero);

        await nordlys.SetAsync(new DoorStateDto(door.Value, DoorState.Unlocked, at, at), ct);

        var own = await nordlys.GetManyAsync([door, missing], ct);
        Assert.Equal(DoorState.Unlocked, Assert.Single(own).Value.State);
        Assert.Empty(await other.GetManyAsync([door], ct));
    }

    [Fact]
    public async Task Older_state_does_not_overwrite_newer()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new InMemoryDoorStateStore(new InMemoryDoorStateCache(), new FixedTenantContext("nordlys"));
        var door = DoorId.New();
        var at = new DateTimeOffset(2026, 11, 10, 8, 0, 0, TimeSpan.Zero);

        Assert.True(await store.SetAsync(new DoorStateDto(door.Value, DoorState.Locked, at, at.AddSeconds(10)), ct));
        Assert.False(await store.SetAsync(new DoorStateDto(door.Value, DoorState.Unlocked, at, at.AddSeconds(5)), ct));

        Assert.Equal(DoorState.Locked, (await store.GetManyAsync([door], ct))[door].State);
    }

    [Fact]
    public async Task Occupancy_counts_each_event_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new InMemoryOccupancyStore(new InMemoryOccupancyStore.Counters(), new FixedTenantContext("nordlys"));
        var area = AreaId.New();
        var day = new DateOnly(2026, 11, 10);
        var swipe = Guid.NewGuid();

        Assert.Equal(1, await store.AdjustAsync(swipe, area, day, null, 1, ct));
        Assert.Equal(1, await store.AdjustAsync(swipe, area, day, null, 1, ct));
        Assert.Equal(2, await store.AdjustAsync(Guid.NewGuid(), area, day, null, 1, ct));
    }
}
