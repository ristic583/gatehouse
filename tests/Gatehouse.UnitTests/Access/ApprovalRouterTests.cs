using Gatehouse.Domain.Access;
using Gatehouse.Domain.Sites;

namespace Gatehouse.UnitTests.Access;

public sealed class ApprovalRouterTests
{
    [Fact]
    public void Both_routes_manager_then_area_owner()
    {
        Assert.Equal([ApproverKind.Manager, ApproverKind.AreaOwner], ApprovalRouter.BuildSteps(ApproverChain.Both));
    }

    [Fact]
    public void Single_chains_produce_one_step()
    {
        Assert.Equal([ApproverKind.Manager], ApprovalRouter.BuildSteps(ApproverChain.Manager));
        Assert.Equal([ApproverKind.AreaOwner], ApprovalRouter.BuildSteps(ApproverChain.AreaOwner));
    }
}
