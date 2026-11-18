using Gatehouse.Domain.Sites;

namespace Gatehouse.Domain.Access;

public static class ApprovalRouter
{
    public static IReadOnlyList<ApproverKind> BuildSteps(ApproverChain chain) => chain switch
    {
        ApproverChain.Manager => [ApproverKind.Manager],
        ApproverChain.AreaOwner => [ApproverKind.AreaOwner],
        ApproverChain.Both => [ApproverKind.Manager, ApproverKind.AreaOwner],
        _ => throw new ArgumentOutOfRangeException(nameof(chain), chain, "Unknown approver chain."),
    };
}
