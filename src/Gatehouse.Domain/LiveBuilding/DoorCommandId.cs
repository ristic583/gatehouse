namespace Gatehouse.Domain.LiveBuilding;

public readonly record struct DoorCommandId(Guid Value)
{
    public static DoorCommandId New() => new(Guid.CreateVersion7());
}
