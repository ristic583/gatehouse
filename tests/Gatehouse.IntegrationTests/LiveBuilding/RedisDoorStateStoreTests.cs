using Gatehouse.Application.LiveBuilding.Dtos;
using Gatehouse.Domain.LiveBuilding;
using Gatehouse.Domain.Sites;
using Gatehouse.Infrastructure.LiveBuilding;
using Gatehouse.IntegrationTests.Infrastructure;

namespace Gatehouse.IntegrationTests.LiveBuilding;

/// <summary>The Lua compare-and-set of <see cref="RedisDoorStateStore"/> against real Redis (GH-LIVE-04).</summary>
public sealed class RedisDoorStateStoreTests(RedisFixture redis)
{
    private static readonly DateTimeOffset At = new(2026, 11, 10, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Older_state_does_not_overwrite_newer()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = NewStore(RedisFixture.UniqueTenantId());
        var door = DoorId.New();

        Assert.True(await store.SetAsync(new DoorStateDto(door.Value, DoorState.Locked, At, At.AddSeconds(10)), ct));
        Assert.False(await store.SetAsync(new DoorStateDto(door.Value, DoorState.Unlocked, At, At.AddSeconds(5)), ct));

        Assert.Equal(DoorState.Locked, (await store.GetManyAsync([door], ct))[door].State);
    }

    [Fact]
    public async Task Equal_timestamp_is_idempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = NewStore(RedisFixture.UniqueTenantId());
        var door = DoorId.New();
        var state = new DoorStateDto(door.Value, DoorState.Open, At, At.AddSeconds(10));

        Assert.True(await store.SetAsync(state, ct));
        Assert.True(await store.SetAsync(state, ct));

        var stored = (await store.GetManyAsync([door], ct))[door];
        Assert.Equal(DoorState.Open, stored.State);
        Assert.Equal(state.LastEventAt, stored.LastEventAt);
    }

    [Fact]
    public async Task Keys_are_tenant_prefixed()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = RedisFixture.UniqueTenantId();
        var store = NewStore(tenant);
        var other = NewStore(RedisFixture.UniqueTenantId());
        var door = DoorId.New();

        await store.SetAsync(new DoorStateDto(door.Value, DoorState.Unlocked, At, At), ct);

        var server = redis.Connection.GetServer(redis.Connection.GetEndPoints()[0]);
        var keys = server.Keys(pattern: $"*{door.Value}*").Select(k => k.ToString()).ToList();
        Assert.Equal(2, keys.Count);
        Assert.All(keys, k => Assert.StartsWith($"tenant:{tenant}:", k, StringComparison.Ordinal));
        Assert.Single(await store.GetManyAsync([door, DoorId.New()], ct));
        Assert.Empty(await other.GetManyAsync([door], ct));
    }

    private RedisDoorStateStore NewStore(string tenant) => new(redis.Connection, new RedisTestTenant(tenant));
}
