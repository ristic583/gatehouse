using Gatehouse.Domain.Fulfillment;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using FulfillmentEntity = Gatehouse.Domain.Fulfillment.Fulfillment;

namespace Gatehouse.IntegrationTests.Persistence;

public sealed class FulfillmentMappingTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Duplicate_fulfillment_violates_unique_index()
    {
        var ct = TestContext.Current.CancellationToken;
        var testDb = await TenantTestDb.CreateAsync(postgres, ct);
        await using var db = testDb.NewContext();
        var (entitlement, _, _, area) = AccessData.Seed(db);
        var pacs = PacsInstance.Create("HQ", PacsVendor.LegacyPacs, []).Value;
        db.PacsInstances.Add(pacs);
        db.AccessLevelMappings.Add(AccessLevelMapping.Create(area.Id, pacs.Id, "AL-1", "Level 1").Value);
        await db.SaveChangesAsync(ct);

        var loadedPacs = await db.PacsInstances.AsNoTracking().SingleAsync(p => p.Id == pacs.Id, ct);
        Assert.Empty(loadedPacs.SiteIds);

        db.Fulfillments.Add(FulfillmentEntity.Create(entitlement.Id, pacs.Id, FulfillmentKind.Grant, CommandPriority.Normal, MappingData.Now));
        db.Fulfillments.Add(FulfillmentEntity.Create(entitlement.Id, pacs.Id, FulfillmentKind.Grant, CommandPriority.Normal, MappingData.Now));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
    }
}
