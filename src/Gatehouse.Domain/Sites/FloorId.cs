namespace Gatehouse.Domain.Sites;

public readonly record struct FloorId(Guid Value)
{
    public static FloorId New() => new(Guid.CreateVersion7());
}
