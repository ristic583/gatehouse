using Gatehouse.Application.Authorization.Handlers;
using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;
using static Gatehouse.UnitTests.Authorization.AuthTestKit;

namespace Gatehouse.UnitTests.Authorization;

public sealed class OperateDoorsRequirementTests
{
    [Fact]
    public void Site_scope_is_enforced()
    {
        var other = SiteId.New();
        var siteStaff = new SecurityStaffScopeHandler(new FakeCurrentUser(PersonId.New(), staff: true, staffSites: new HashSet<SiteId> { AuthTestKit.Site }));
        var tenantStaff = new SecurityStaffScopeHandler(new FakeCurrentUser(PersonId.New(), staff: true));
        var admin = new TenantAdminHandler(new FakeCurrentUser(PersonId.New(), admin: true));
        var adminViaStaffHandler = new SecurityStaffScopeHandler(new FakeCurrentUser(PersonId.New(), admin: true));

        Assert.True(Run(siteStaff, new OperateDoorsRequirement(), AuthTestKit.Site).HasSucceeded);
        Assert.False(Run(siteStaff, new OperateDoorsRequirement(), other).HasSucceeded);
        Assert.True(Run(tenantStaff, new OperateDoorsRequirement(), other).HasSucceeded);
        Assert.False(Run(admin, new OperateDoorsRequirement(), AuthTestKit.Site).HasSucceeded);
        Assert.False(Run(adminViaStaffHandler, new OperateDoorsRequirement(), AuthTestKit.Site).HasSucceeded);
    }
}
