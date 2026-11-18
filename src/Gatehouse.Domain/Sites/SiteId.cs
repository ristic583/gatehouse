namespace Gatehouse.Domain.Sites;

public readonly record struct SiteId(Guid Value)
{
    public static SiteId New() => new(Guid.CreateVersion7());
}
