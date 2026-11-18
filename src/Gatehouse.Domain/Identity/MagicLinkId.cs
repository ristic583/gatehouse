namespace Gatehouse.Domain.Identity;

public readonly record struct MagicLinkId(Guid Value)
{
    public static MagicLinkId New() => new(Guid.CreateVersion7());
}
