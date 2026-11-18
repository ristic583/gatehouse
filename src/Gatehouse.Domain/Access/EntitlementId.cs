namespace Gatehouse.Domain.Access;

public readonly record struct EntitlementId(Guid Value)
{
    public static EntitlementId New() => new(Guid.CreateVersion7());
}
