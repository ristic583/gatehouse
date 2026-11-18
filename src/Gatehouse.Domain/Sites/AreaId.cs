namespace Gatehouse.Domain.Sites;

public readonly record struct AreaId(Guid Value)
{
    public static AreaId New() => new(Guid.CreateVersion7());
}
