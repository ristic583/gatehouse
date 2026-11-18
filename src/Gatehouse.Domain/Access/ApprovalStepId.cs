namespace Gatehouse.Domain.Access;

public readonly record struct ApprovalStepId(Guid Value)
{
    public static ApprovalStepId New() => new(Guid.CreateVersion7());
}
