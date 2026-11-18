using Gatehouse.Domain.Sites;

namespace Gatehouse.UnitTests.Sites;

public sealed class FloorTests
{
    [Fact]
    public void Update_validates_level()
    {
        var floor = Floor.Create(SiteId.New(), "Ground", 0, 1000, 800).Value;

        var invalid = floor.Update("Ground", 201, 1000, 800);
        var valid = floor.Update("Second", 2, 1200, 900);

        Assert.Equal("floor.invalid_level", invalid.Error.Code);
        Assert.True(valid.IsSuccess);
        Assert.Equal((2, 1200, 900), (floor.Level, floor.PlanWidth, floor.PlanHeight));
    }

    [Theory]
    [InlineData(99, 600)]
    [InlineData(1000, 10001)]
    public void Plan_size_out_of_range_is_rejected(int width, int height)
    {
        var result = Floor.Create(SiteId.New(), "Ground", 0, width, height);
        Assert.Equal("floor.invalid_plan_size", result.Error.Code);
    }
}
