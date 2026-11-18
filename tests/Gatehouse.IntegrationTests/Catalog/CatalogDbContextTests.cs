using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tenancy;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.IntegrationTests.Catalog;

public sealed class CatalogDbContextTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Migrations_apply_and_tenant_round_trips()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await CatalogTestDb.CreateMigratedAsync(postgres, ct);

        await using (var db = CatalogTestDb.Open(connectionString))
        {
            db.Tenants.Add(CatalogTestDb.NewTenant("nordlys", "Nordlys A/S", IdpKind.Oidc));
            await db.SaveChangesAsync(ct);
        }

        await using (var db = CatalogTestDb.Open(connectionString))
        {
            var tenant = await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == new TenantId("nordlys"), ct);

            Assert.Equal("Nordlys A/S", tenant.DisplayName);
            Assert.Equal(IdpKind.Oidc, tenant.IdpKind);
            Assert.Equal(TenantStatus.Active, tenant.Status);
            Assert.Equal("gh_tenant_nordlys", tenant.DatabaseName);
            Assert.Equal("nordlys", tenant.KeycloakOrganizationAlias);
            Assert.Equal(CatalogTestDb.Now, tenant.CreatedAt);
        }
    }
}
