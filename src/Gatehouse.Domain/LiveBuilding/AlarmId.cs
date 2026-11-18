namespace Gatehouse.Domain.LiveBuilding;

public readonly record struct AlarmId(Guid Value)
{
    public static AlarmId New() => new(Guid.CreateVersion7());
}
