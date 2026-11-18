using Gatehouse.Application.Authorization.Handlers;
using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Application.Authorization.Resources;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;
using static Gatehouse.UnitTests.Authorization.AuthTestKit;

namespace Gatehouse.UnitTests.Authorization;

public sealed class ReconciliationRequirementTests
{
    private static readonly SiteId Other = SiteId.New();

    private static PacsInstanceResource Instance(params SiteId[] sites) => new(PacsInstanceId.New(), sites.ToHashSet());

    [Fact]
    public void Tenant_admin_can_view_not_run()
    {
        var admin = new TenantAdminHandler(new FakeCurrentUser(PersonId.New(), admin: true));
        var adminViaStaffHandler = new SecurityStaffScopeHandler(new FakeCurrentUser(PersonId.New(), admin: true));
        var instance = Instance(AuthTestKit.Site);

        Assert.True(Run(admin, new ViewReconciliationRequirement(), instance).HasSucceeded);
        Assert.False(Run(admin, new RunReconciliationRequirement(), instance).HasSucceeded);
        Assert.False(Run(adminViaStaffHandler, new RunReconciliationRequirement(), instance).HasSucceeded);
        Assert.False(Run(adminViaStaffHandler, new ViewReconciliationRequirement(), instance).HasSucceeded);
    }

    [Fact]
    public void Site_staff_runs_only_when_all_sites_in_scope()
    {
        var siteStaff = new SecurityStaffScopeHandler(new FakeCurrentUser(PersonId.New(), staff: true, staffSites: new HashSet<SiteId> { AuthTestKit.Site }));
        var own = Instance(AuthTestKit.Site);
        var shared = Instance(AuthTestKit.Site, Other);
        var foreign = Instance(Other);

        Assert.True(Run(siteStaff, new ViewReconciliationRequirement(), own).HasSucceeded);
        Assert.True(Run(siteStaff, new RunReconciliationRequirement(), own).HasSucceeded);
        Assert.True(Run(siteStaff, new ViewReconciliationRequirement(), shared).HasSucceeded);
        Assert.False(Run(siteStaff, new RunReconciliationRequirement(), shared).HasSucceeded);
        Assert.False(Run(siteStaff, new ViewReconciliationRequirement(), foreign).HasSucceeded);
        Assert.False(Run(siteStaff, new RunReconciliationRequirement(), foreign).HasSucceeded);
    }

    [Fact]
    public void Tenant_wide_staff_can_run()
    {
        var tenantStaff = new SecurityStaffScopeHandler(new FakeCurrentUser(PersonId.New(), staff: true));
        var instance = Instance(AuthTestKit.Site, Other);

        Assert.True(Run(tenantStaff, new ViewReconciliationRequirement(), instance).HasSucceeded);
        Assert.True(Run(tenantStaff, new RunReconciliationRequirement(), instance).HasSucceeded);
        Assert.True(Run(tenantStaff, new RunReconciliationRequirement(), Instance()).HasSucceeded);
    }
}
