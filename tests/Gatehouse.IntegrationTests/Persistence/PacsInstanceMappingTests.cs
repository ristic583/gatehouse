using Gatehouse.Domain.Fulfillment;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.IntegrationTests.Persistence;

public sealed class PacsInstanceMappingTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Fulfillment_mode_round_trips()
    {
        var ct = TestContext.Current.CancellationToken;
        var testDb = await TenantTestDb.CreateAsync(postgres, ct);
        await using var db = testDb.NewContext();
        var pacs = PacsInstance.Create("HQ", PacsVendor.LegacyPacs, []).Value;
        var manual = PacsInstance.Create("Other", PacsVendor.LegacyPacs, []).Value;
        pacs.SetFulfillmentMode(FulfillmentMode.Connector);
        db.PacsInstances.AddRange(pacs, manual);
        await db.SaveChangesAsync(ct);

        await using var read = testDb.NewContext();
        Assert.Equal(FulfillmentMode.Connector, (await read.PacsInstances.AsNoTracking().SingleAsync(p => p.Id == pacs.Id, ct)).FulfillmentMode);
        Assert.Equal(FulfillmentMode.Manual, (await read.PacsInstances.AsNoTracking().SingleAsync(p => p.Id == manual.Id, ct)).FulfillmentMode);
    }
}
