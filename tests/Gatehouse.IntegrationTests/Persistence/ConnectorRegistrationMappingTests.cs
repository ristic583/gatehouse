using Gatehouse.Domain.Common;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.IntegrationTests.Catalog;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.IntegrationTests.Persistence;

public sealed class ConnectorRegistrationMappingTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Registration_round_trips()
    {
        var ct = TestContext.Current.CancellationToken;
        var cs = await CatalogTestDb.CreateMigratedAsync(postgres, ct);
        var reg = ConnectorRegistration.Create(new TenantId("nordlys"), PacsInstanceId.New(), "HQ", "hash", CatalogTestDb.Now);
        await using (var db = CatalogTestDb.Open(cs))
        {
            db.ConnectorRegistrations.Add(reg);
            await db.SaveChangesAsync(ct);
        }

        await using var read = CatalogTestDb.Open(cs);
        var loaded = await read.ConnectorRegistrations.AsNoTracking().SingleAsync(r => r.Id == reg.Id, ct);
        Assert.Equal(ConnectorStatus.PendingEnrollment, loaded.Status);
        Assert.Equal("hash", loaded.EnrollmentTokenHash);
        Assert.Equal(new TenantId("nordlys"), loaded.TenantId);
    }
}
