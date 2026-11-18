using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.IntegrationTests.Persistence;

public sealed class ConnectorCommandMappingTests(PostgresFixture postgres)
{
    private static ConnectorCommand NewCommand(PacsInstanceId pacs, long seq, string key) =>
        ConnectorCommand.Create(pacs, "GrantAccess", "person:1", seq, CommandPriority.Normal, key, "{\"cardNumber\":\"100200300\"}", null, MappingData.Now);

    [Fact]
    public async Task Command_round_trips_with_jsonb_payload()
    {
        var ct = TestContext.Current.CancellationToken;
        var testDb = await TenantTestDb.CreateAsync(postgres, ct);
        await using var db = testDb.NewContext();
        var pacs = PacsInstance.Create("HQ", PacsVendor.LegacyPacs, []).Value;
        db.PacsInstances.Add(pacs);
        var cmd = NewCommand(pacs.Id, 1, "GrantAccess:a:1");
        db.ConnectorCommands.Add(cmd);
        await db.SaveChangesAsync(ct);

        await using var read = testDb.NewContext();
        var loaded = await read.ConnectorCommands.AsNoTracking().SingleAsync(c => c.Id == cmd.Id, ct);
        Assert.Contains("100200300", loaded.PayloadJson, StringComparison.Ordinal);
        Assert.Equal(ConnectorCommandStatus.Queued, loaded.Status);
        Assert.Equal(CommandPriority.Normal, loaded.Priority);
    }

    [Fact]
    public async Task Duplicate_idempotency_key_violates_unique_index()
    {
        var ct = TestContext.Current.CancellationToken;
        var testDb = await TenantTestDb.CreateAsync(postgres, ct);
        await using var db = testDb.NewContext();
        var pacs = PacsInstance.Create("HQ", PacsVendor.LegacyPacs, []).Value;
        db.PacsInstances.Add(pacs);
        db.ConnectorCommands.Add(NewCommand(pacs.Id, 1, "k:1"));
        db.ConnectorCommands.Add(NewCommand(pacs.Id, 2, "k:1"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
    }
}
