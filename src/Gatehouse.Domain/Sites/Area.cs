using System.Text.RegularExpressions;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;

namespace Gatehouse.Domain.Sites;

public sealed partial class Area : AggregateRoot<AreaId>
{
    private readonly List<AreaOwner> _owners = [];

    private Area(AreaId id, FloorId floorId, string name, string code, Polygon polygon, SecurityLevelId securityLevelId)
        : base(id)
    {
        FloorId = floorId;
        Name = name;
        Code = code;
        Polygon = polygon;
        SecurityLevelId = securityLevelId;
    }

    // EF Core
    private Area()
        : base(default)
    {
        Name = string.Empty;
        Code = string.Empty;
        Polygon = new Polygon([]);
    }

    public FloorId FloorId { get; private set; }

    public string Name { get; private set; }

    public string Code { get; private set; }

    public Polygon Polygon { get; private set; }

    public SecurityLevelId SecurityLevelId { get; private set; }

    public IReadOnlyCollection<AreaOwner> Owners => _owners.AsReadOnly();

    /// <summary>
    /// False for a space without a PACS door (public corridor, storage): excluded from access-level mapping, onboarding and requests
    /// (lab finding #15).
    /// </summary>
    public bool AccessControlled { get; private set; } = true;

    public uint Version { get; private set; }

    public static Result<Area> Create(
        FloorId floorId,
        string name,
        string code,
        Polygon polygon,
        SecurityLevelId levelId,
        PersonId firstOwner,
        PersonType firstOwnerType)
    {
        ArgumentNullException.ThrowIfNull(polygon);

        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
        {
            return Error.Validation("area.invalid_name", "Area name must be 1-100 characters.");
        }

        if (code is null || !CodePattern().IsMatch(code))
        {
            return Error.Validation("area.invalid_code", "Area code must match ^[A-Z0-9-]{2,30}$.");
        }

        var area = new Area(AreaId.New(), floorId, name, code, polygon, levelId);
        var added = area.AddOwner(firstOwner, firstOwnerType);
        return added.IsFailure ? added.Error : area;
    }

    /// <summary>Admin edit (M10): the polygon is re-validated against the floor plan size.</summary>
    public Result Update(
        string name, string code, IEnumerable<Point> polygon, SecurityLevelId securityLevelId, int planWidth, int planHeight)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
        {
            return Error.Validation("area.invalid_name", "Area name must be 1-100 characters.");
        }

        if (code is null || !CodePattern().IsMatch(code))
        {
            return Error.Validation("area.invalid_code", "Area code must match ^[A-Z0-9-]{2,30}$.");
        }

        var checkedPolygon = Polygon.Create(polygon, planWidth, planHeight);
        if (checkedPolygon.IsFailure)
        {
            return checkedPolygon.Error;
        }

        Name = name;
        Code = code;
        Polygon = checkedPolygon.Value;
        SecurityLevelId = securityLevelId;
        return Result.Success();
    }

    /// <summary>Replaces the owner set: at least one, all employees (same rules as <see cref="AddOwner"/>/<see
    /// cref="RemoveOwner"/>).</summary>
    public void SetAccessControlled(bool accessControlled) => AccessControlled = accessControlled;

    public Result ReplaceOwners(IReadOnlyCollection<(PersonId PersonId, PersonType Type)> owners)
    {
        ArgumentNullException.ThrowIfNull(owners);
        if (owners.Count == 0)
        {
            return Error.Conflict("area.last_owner", "An area must keep at least one owner.");
        }

        if (owners.Any(o => o.Type != PersonType.Employee))
        {
            return Error.Validation("area.owner_not_employee", "An area owner must be an employee.");
        }

        foreach (var (personId, type) in owners)
        {
            _ = AddOwner(personId, type);
        }

        var keep = owners.Select(o => o.PersonId).ToHashSet();
        _owners.RemoveAll(o => !keep.Contains(o.PersonId));
        return Result.Success();
    }

    public Result AddOwner(PersonId personId, PersonType personType)
    {
        if (personType != PersonType.Employee)
        {
            return Error.Validation("area.owner_not_employee", "An area owner must be an employee.");
        }

        if (_owners.All(o => o.PersonId != personId))
        {
            _owners.Add(new AreaOwner(personId));
        }

        return Result.Success();
    }

    public Result RemoveOwner(PersonId personId)
    {
        var owner = _owners.FirstOrDefault(o => o.PersonId == personId);
        if (owner is null)
        {
            return Result.Success();
        }

        if (_owners.Count == 1)
        {
            return Error.Conflict("area.last_owner", "An area must keep at least one owner.");
        }

        _owners.Remove(owner);
        return Result.Success();
    }

    [GeneratedRegex("^[A-Z0-9-]{2,30}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}
