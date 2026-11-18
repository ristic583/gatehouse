using Gatehouse.Domain.Sites;
using Gatehouse.Infrastructure.LiveBuilding;

namespace Gatehouse.UnitTests.LiveBuilding;

/// <summary>Lab finding #21: occupancy counts distinct people, not In swipes.</summary>
public sealed class InMemoryOccupancyStoreTests
{
    private static readonly DateOnly Day = new(2026, 11, 10);

    [Fact]
    public async Task Same_person_in_twice_counts_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = NewStore();
        var area = AreaId.New();

        Assert.Equal(1, await store.AdjustAsync(Guid.NewGuid(), area, Day, "person:anna", 1, ct));
        Assert.Equal(1, await store.AdjustAsync(Guid.NewGuid(), area, Day, "person:anna", 1, ct));
        Assert.Equal(2, await store.AdjustAsync(Guid.NewGuid(), area, Day, "card:abc", 1, ct));
        Assert.Equal(1, await store.AdjustAsync(Guid.NewGuid(), area, Day, "person:anna", -1, ct));

        Assert.Equal(1, (await store.GetAsync([area], Day, ct))[area]);
    }

    [Fact]
    public async Task Out_without_in_does_not_go_negative()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = NewStore();
        var area = AreaId.New();

        Assert.Equal(0, await store.AdjustAsync(Guid.NewGuid(), area, Day, "person:anna", -1, ct));
        Assert.Equal(0, await store.AdjustAsync(Guid.NewGuid(), area, Day, null, -1, ct));
        Assert.Equal(1, await store.AdjustAsync(Guid.NewGuid(), area, Day, "person:anna", 1, ct));
        Assert.Equal(2, await store.AdjustAsync(Guid.NewGuid(), area, Day, null, 1, ct));
    }

    private static InMemoryOccupancyStore NewStore() => new(new InMemoryOccupancyStore.Counters(), new FixedTenantContext("nordlys"));
}
