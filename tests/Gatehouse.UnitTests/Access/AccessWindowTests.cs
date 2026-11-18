using Gatehouse.Domain.Access;

namespace Gatehouse.UnitTests.Access;

public sealed class AccessWindowTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    private static AccessWindow W(int fromDay, int toDay) => new(T0.AddDays(fromDay), T0.AddDays(toDay));

    [Fact]
    public void Current_with_no_windows_is_null() => Assert.Null(AccessWindow.Current([]));

    [Fact]
    public void Current_merges_adjacent_renewal_into_current()
    {
        Assert.Equal(W(0, 90), AccessWindow.Current([W(30, 90), W(0, 30)]));
    }

    [Fact]
    public void Current_merges_overlapping_chain_transitively()
    {
        Assert.Equal(W(0, 50), AccessWindow.Current([W(0, 10), W(20, 50), W(5, 25)]));
    }

    [Fact]
    public void Current_keeps_earliest_window_when_later_one_has_a_gap()
    {
        Assert.Equal(W(0, 10), AccessWindow.Current([W(20, 40), W(0, 10)]));
    }
}
