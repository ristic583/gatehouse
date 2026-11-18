using Gatehouse.Domain.Common;

namespace Gatehouse.Domain.Sites;

public sealed class Floor : AggregateRoot<FloorId>
{
    private Floor(FloorId id, SiteId siteId, string name, int level, int planWidth, int planHeight)
        : base(id)
    {
        SiteId = siteId;
        Name = name;
        Level = level;
        PlanWidth = planWidth;
        PlanHeight = planHeight;
    }

    public SiteId SiteId { get; private set; }

    public string Name { get; private set; }

    public int Level { get; private set; }

    public int PlanWidth { get; private set; }

    public int PlanHeight { get; private set; }

    /// <summary>M10 floor drawing (stretched to the plan); all <c>Background*</c> fields are set together or null.</summary>
    public string? BackgroundObjectKey { get; private set; }

    public string? BackgroundContentType { get; private set; }

    public int? BackgroundWidthPx { get; private set; }

    public int? BackgroundHeightPx { get; private set; }

    public DateTimeOffset? BackgroundUpdatedAt { get; private set; }

    public bool HasBackground => BackgroundObjectKey is not null;

    public void SetBackground(string objectKey, string contentType, int widthPx, int heightPx, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        BackgroundObjectKey = objectKey;
        BackgroundContentType = contentType;
        BackgroundWidthPx = widthPx;
        BackgroundHeightPx = heightPx;
        BackgroundUpdatedAt = now;
    }

    public void ClearBackground()
    {
        BackgroundObjectKey = null;
        BackgroundContentType = null;
        BackgroundWidthPx = null;
        BackgroundHeightPx = null;
        BackgroundUpdatedAt = null;
    }

    public static Result<Floor> Create(SiteId siteId, string name, int level, int planWidth, int planHeight)
    {
        var valid = Validate(name, level, planWidth, planHeight);
        return valid.IsFailure ? valid.Error : new Floor(FloorId.New(), siteId, name, level, planWidth, planHeight);
    }

    /// <summary>Shrinking below existing geometry is checked by the caller (<c>floor.invalid_plan_size</c>).</summary>
    public Result Update(string name, int level, int planWidth, int planHeight)
    {
        var valid = Validate(name, level, planWidth, planHeight);
        if (valid.IsFailure)
        {
            return valid;
        }

        Name = name;
        Level = level;
        PlanWidth = planWidth;
        PlanHeight = planHeight;
        return Result.Success();
    }

    private static Result Validate(string name, int level, int planWidth, int planHeight)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
        {
            return Error.Validation("floor.invalid_name", "Floor name must be 1-100 characters.");
        }

        if (level is < -5 or > 200)
        {
            return Error.Validation("floor.invalid_level", "Floor level must be between -5 and 200.");
        }

        if (planWidth is < 100 or > 10000 || planHeight is < 100 or > 10000)
        {
            return Error.Validation("floor.invalid_plan_size", "Plan width and height must be 100-10000.");
        }

        return Result.Success();
    }
}
