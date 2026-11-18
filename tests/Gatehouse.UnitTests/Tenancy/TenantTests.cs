using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tenancy;

namespace Gatehouse.UnitTests.Tenancy;

public sealed class TenantTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_sets_database_name_and_alias()
    {
        var tenant = Tenant.Create(TenantId.Create("acme-north").Value, "Acme North", IdpKind.Oidc, "v1:abc", Now).Value;

        Assert.Equal("gh_tenant_acme_north", tenant.DatabaseName);
        Assert.Equal("acme-north", tenant.KeycloakOrganizationAlias);
        Assert.Equal(TenantStatus.Active, tenant.Status);
        Assert.Equal(Now, tenant.CreatedAt);
    }

    [Fact]
    public void Suspend_then_activate_round_trips()
    {
        var tenant = Tenant.Create(TenantId.Create("nordlys").Value, "Nordlys A/S", IdpKind.Oidc, "v1:abc", Now).Value;

        tenant.Suspend("test", Now);
        Assert.Equal(TenantStatus.Suspended, tenant.Status);

        tenant.Reactivate();
        Assert.Equal(TenantStatus.Active, tenant.Status);
    }
}
