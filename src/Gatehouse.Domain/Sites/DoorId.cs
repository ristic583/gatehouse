namespace Gatehouse.Domain.Sites;

public readonly record struct DoorId(Guid Value)
{
    public static DoorId New() => new(Guid.CreateVersion7());
}
