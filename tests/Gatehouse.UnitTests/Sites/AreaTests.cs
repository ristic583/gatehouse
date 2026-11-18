using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;

namespace Gatehouse.UnitTests.Sites;

public sealed class AreaTests
{
    private static readonly Polygon Rect = Polygon.Create([new(0, 0), new(10, 0), new(10, 10), new(0, 10)], 100, 100).Value;

    [Fact]
    public void Update_rejects_polygon_outside_plan()
    {
        var area = Area.Create(FloorId.New(), "Lab", "CPH-LAB", Rect, SecurityLevelId.New(), PersonId.New(), PersonType.Employee).Value;

        var outside = area.Update("Lab", "CPH-LAB", [new(0, 0), new(150, 0), new(150, 10), new(0, 10)], area.SecurityLevelId, 100, 100);
        var inside = area.Update("Lab 2", "CPH-LAB2", [new(0, 0), new(50, 0), new(50, 50), new(0, 50)], area.SecurityLevelId, 100, 100);

        Assert.Equal("area.invalid_polygon", outside.Error.Code);
        Assert.True(inside.IsSuccess);
        Assert.Equal(("Lab 2", 4), (area.Name, area.Polygon.Points.Count));
    }

    [Fact]
    public void Replace_owners_keeps_at_least_one()
    {
        var first = PersonId.New();
        var second = PersonId.New();
        var area = Area.Create(FloorId.New(), "Lab", "CPH-LAB", Rect, SecurityLevelId.New(), first, PersonType.Employee).Value;

        var empty = area.ReplaceOwners([]);
        var external = area.ReplaceOwners([(second, PersonType.External)]);
        var replaced = area.ReplaceOwners([(second, PersonType.Employee)]);

        Assert.Equal("area.last_owner", empty.Error.Code);
        Assert.Equal("area.owner_not_employee", external.Error.Code);
        Assert.True(replaced.IsSuccess);
        Assert.Equal(second, Assert.Single(area.Owners).PersonId);
    }

    [Fact]
    public void Non_employee_owner_is_rejected()
    {
        var result = Area.Create(FloorId.New(), "Lab", "CPH-LAB", Rect, SecurityLevelId.New(), PersonId.New(), PersonType.External);
        Assert.Equal("area.owner_not_employee", result.Error.Code);
    }

    [Fact]
    public void Removing_last_owner_fails()
    {
        var owner = PersonId.New();
        var area = Area.Create(FloorId.New(), "Lab", "CPH-LAB", Rect, SecurityLevelId.New(), owner, PersonType.Employee).Value;

        var result = area.RemoveOwner(owner);

        Assert.Equal("area.last_owner", result.Error.Code);
        Assert.Single(area.Owners);
    }

    [Fact]
    public void Adding_second_owner_then_removing_first_succeeds()
    {
        var first = PersonId.New();
        var second = PersonId.New();
        var area = Area.Create(FloorId.New(), "Lab", "CPH-LAB", Rect, SecurityLevelId.New(), first, PersonType.Employee).Value;

        Assert.True(area.AddOwner(second, PersonType.Employee).IsSuccess);
        Assert.True(area.RemoveOwner(first).IsSuccess);

        Assert.Equal(second, Assert.Single(area.Owners).PersonId);
    }
}
