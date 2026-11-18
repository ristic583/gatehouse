using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;

namespace Gatehouse.UnitTests.People;

public sealed class RoleAssignmentTests
{
    [Fact]
    public void Tenant_admin_cannot_be_site_scoped()
    {
        var result = RoleAssignment.Create(PersonId.New(), AssignedRole.TenantAdmin, SiteId.New());
        Assert.Equal("role.tenant_admin_cannot_be_site_scoped", result.Error.Code);
        Assert.True(RoleAssignment.Create(PersonId.New(), AssignedRole.SecurityStaff, SiteId.New()).IsSuccess);
    }
}
