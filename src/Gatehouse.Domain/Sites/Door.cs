using Gatehouse.Domain.Common;

namespace Gatehouse.Domain.Sites;

public sealed class Door : Entity<DoorId>
{
    private Door(DoorId id, AreaId areaId, string name, Point position, string? pacsDoorRef)
        : base(id)
    {
        AreaId = areaId;
        Name = name;
        Position = position;
        PacsDoorRef = pacsDoorRef;
    }

    // EF Core
    private Door()
        : base(default)
    {
        Name = string.Empty;
    }

    public AreaId AreaId { get; private set; }

    public string Name { get; private set; }

    public Point Position { get; private set; }

    public string? PacsDoorRef { get; private set; }

    public Result Update(string name, Point position, string? pacsDoorRef)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
        {
            return Error.Validation("door.invalid_name", "Door name must be 1-100 characters.");
        }

        Name = name;
        Position = position;
        PacsDoorRef = pacsDoorRef;
        return Result.Success();
    }

    /// <summary>Attaches the door to another area of the same floor (the caller checks the floor; lab finding #7).</summary>
    public void MoveTo(AreaId areaId) => AreaId = areaId;

    public static Result<Door> Create(AreaId areaId, string name, Point position, string? pacsDoorRef)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
        {
            return Error.Validation("door.invalid_name", "Door name must be 1-100 characters.");
        }

        return new Door(DoorId.New(), areaId, name, position, pacsDoorRef);
    }
}
