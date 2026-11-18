using Gatehouse.Application.Authorization.Handlers;
using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;
using static Gatehouse.UnitTests.Authorization.AuthTestKit;

namespace Gatehouse.UnitTests.Authorization;

public sealed class ViewLiveSiteRequirementTests
{
    [Fact]
    public void Site_scoped_staff_only_sees_own_sites()
    {
        var other = SiteId.New();
        var siteStaff = new SecurityStaffScopeHandler(new FakeCurrentUser(PersonId.New(), staff: true, staffSites: new HashSet<SiteId> { AuthTestKit.Site }));
        var tenantStaff = new SecurityStaffScopeHandler(new FakeCurrentUser(PersonId.New(), staff: true));
        var admin = new TenantAdminHandler(new FakeCurrentUser(PersonId.New(), admin: true));
        var requester = new SecurityStaffScopeHandler(new FakeCurrentUser(PersonId.New()));

        Assert.True(Run(siteStaff, new ViewLiveSiteRequirement(), AuthTestKit.Site).HasSucceeded);
        Assert.False(Run(siteStaff, new ViewLiveSiteRequirement(), other).HasSucceeded);
        Assert.True(Run(tenantStaff, new ViewLiveSiteRequirement(), other).HasSucceeded);
        Assert.True(Run(admin, new ViewLiveSiteRequirement(), other).HasSucceeded);
        Assert.False(Run(requester, new ViewLiveSiteRequirement(), AuthTestKit.Site).HasSucceeded);
    }
}
