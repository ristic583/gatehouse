using Gatehouse.Domain.Common;

namespace Gatehouse.UnitTests.Common;

public sealed class DateRangeTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void End_is_exclusive()
    {
        var range = new DateRange(Start, Start.AddDays(1));
        Assert.True(range.Contains(Start));
        Assert.False(range.Contains(Start.AddDays(1)));
    }

    [Fact]
    public void Open_end_contains_future()
    {
        var range = new DateRange(Start, null);
        Assert.True(range.Contains(Start.AddYears(50)));
        Assert.False(range.Contains(Start.AddTicks(-1)));
    }
}
