namespace Gatehouse.Domain.Sites;

public readonly record struct SecurityLevelId(Guid Value)
{
    public static SecurityLevelId New() => new(Guid.CreateVersion7());
}
