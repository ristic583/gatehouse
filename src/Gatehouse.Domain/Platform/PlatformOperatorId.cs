namespace Gatehouse.Domain.Platform;

public readonly record struct PlatformOperatorId(Guid Value)
{
    public static PlatformOperatorId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
