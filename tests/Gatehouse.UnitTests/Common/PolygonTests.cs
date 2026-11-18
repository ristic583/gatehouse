using Gatehouse.Domain.Common;

namespace Gatehouse.UnitTests.Common;

public sealed class PolygonTests
{
    [Fact]
    public void Fewer_than_three_points_is_invalid()
    {
        var result = Polygon.Create([new(0, 0), new(10, 0)], 100, 100);
        Assert.Equal("area.invalid_polygon", result.Error.Code);
    }

    [Fact]
    public void Point_outside_plan_is_invalid()
    {
        var result = Polygon.Create([new(0, 0), new(150, 0), new(10, 10)], 100, 100);
        Assert.Equal("area.invalid_polygon", result.Error.Code);
    }

    [Fact]
    public void Self_intersecting_polygon_is_invalid()
    {
        var result = Polygon.Create([new(0, 0), new(10, 10), new(10, 0), new(0, 10)], 100, 100);
        Assert.Equal("area.invalid_polygon", result.Error.Code);
    }

    [Fact]
    public void Rectangle_is_valid()
    {
        var result = Polygon.Create([new(0, 0), new(100, 0), new(100, 50), new(0, 50)], 100, 100);
        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value.Points.Count);
    }
}
