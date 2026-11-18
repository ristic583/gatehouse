namespace Gatehouse.Domain.People;

public readonly record struct RoleAssignmentId(Guid Value)
{
    public static RoleAssignmentId New() => new(Guid.CreateVersion7());
}
