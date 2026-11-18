namespace Gatehouse.Domain.Tasks;

public readonly record struct StaffTaskId(Guid Value)
{
    public static StaffTaskId New() => new(Guid.CreateVersion7());
}
