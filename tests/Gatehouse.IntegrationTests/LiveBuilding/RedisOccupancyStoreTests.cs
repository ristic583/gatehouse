using Gatehouse.Domain.Sites;
using Gatehouse.Infrastructure.LiveBuilding;
using Gatehouse.IntegrationTests.Infrastructure;

namespace Gatehouse.IntegrationTests.LiveBuilding;

/// <summary>The Lua script of <see cref="RedisOccupancyStore"/> against real Redis (GH-LIVE-05, lab #21).</summary>
public sealed class RedisOccupancyStoreTests(RedisFixture redis)
{
    private static readonly DateOnly Day = new(2026, 11, 10);

    [Fact]
    public async Task Occupancy_counts_each_event_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = NewStore(RedisFixture.UniqueTenantId());
        var area = AreaId.New();
        var swipe = Guid.NewGuid();

        Assert.Equal(1, await store.AdjustAsync(swipe, area, Day, null, 1, ct));
        Assert.Equal(1, await store.AdjustAsync(swipe, area, Day, null, 1, ct));
        Assert.Equal(2, await store.AdjustAsync(Guid.NewGuid(), area, Day, null, 1, ct));

        Assert.Equal(2, (await store.GetAsync([area], Day, ct))[area]);
    }

    [Fact]
    public async Task Same_person_in_twice_counts_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = NewStore(RedisFixture.UniqueTenantId());
        var area = AreaId.New();

        Assert.Equal(1, await store.AdjustAsync(Guid.NewGuid(), area, Day, "person:anna", 1, ct));
        Assert.Equal(1, await store.AdjustAsync(Guid.NewGuid(), area, Day, "person:anna", 1, ct));
        Assert.Equal(2, await store.AdjustAsync(Guid.NewGuid(), area, Day, "card:abc", 1, ct));
        Assert.Equal(3, await store.AdjustAsync(Guid.NewGuid(), area, Day, null, 1, ct));
        Assert.Equal(2, await store.AdjustAsync(Guid.NewGuid(), area, Day, "person:anna", -1, ct));
        Assert.Equal(2, await store.AdjustAsync(Guid.NewGuid(), area, Day, "person:anna", -1, ct));

        Assert.Equal(2, (await store.GetAsync([area], Day, ct))[area]);
    }

    [Fact]
    public async Task Count_never_goes_below_zero()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = NewStore(RedisFixture.UniqueTenantId());
        var area = AreaId.New();

        Assert.Equal(0, await store.AdjustAsync(Guid.NewGuid(), area, Day, null, -1, ct));
        Assert.Equal(1, await store.AdjustAsync(Guid.NewGuid(), area, Day, null, 1, ct));
        Assert.Equal(0, await store.AdjustAsync(Guid.NewGuid(), area, Day, null, -1, ct));
        Assert.Equal(0, await store.AdjustAsync(Guid.NewGuid(), area, Day, null, -1, ct));

        Assert.Equal(0, (await store.GetAsync([area], Day, ct))[area]);
    }

    [Fact]
    public async Task Keys_expire_after_48_hours()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = RedisFixture.UniqueTenantId();
        var store = NewStore(tenant);
        var area = AreaId.New();
        var swipe = Guid.NewGuid();

        await store.AdjustAsync(swipe, area, Day, "person:anna", 1, ct);
        await store.AdjustAsync(Guid.NewGuid(), area, Day, null, 1, ct);

        var db = redis.Connection.GetDatabase();
        foreach (var key in new[]
        {
            $"tenant:{tenant}:presence:20261110:{area.Value}",
            $"tenant:{tenant}:presence-anon:20261110:{area.Value}",
            $"tenant:{tenant}:occupancy-event:{swipe}",
        })
        {
            var ttl = await db.KeyTimeToLiveAsync(key);
            Assert.NotNull(ttl);
            Assert.InRange(ttl.Value, TimeSpan.FromHours(47.9), TimeSpan.FromHours(48));
        }
    }

    private RedisOccupancyStore NewStore(string tenant) => new(redis.Connection, new RedisTestTenant(tenant));
}
