using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Reconciliation;
using Gatehouse.Domain.Sites;
using Gatehouse.Domain.Tasks;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.IntegrationTests.Persistence;

public sealed class ReconciliationMappingTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = MappingData.Now;

    private static ReconciliationReport Report(TenantDbContext db)
    {
        var pacs = PacsInstance.Create("HQ", PacsVendor.LegacyPacs, []).Value;
        db.PacsInstances.Add(pacs);
        var report = ReconciliationReport.Request(pacs.Id, PersonId.New(), CommandId.New(), Now);
        db.ReconciliationReports.Add(report);
        return report;
    }

    [Fact]
    public async Task Report_round_trips_with_coverage()
    {
        var ct = TestContext.Current.CancellationToken;
        var testDb = await TenantTestDb.CreateAsync(postgres, ct);
        var coverage = new ReconciliationCoverage([Guid.NewGuid()], [Guid.NewGuid(), Guid.NewGuid()], ["L1", "L2"], ["AccessAssignment"], 10, 8, 6, 1, 1);
        ReconciliationReport report;
        await using (var db = testDb.NewContext())
        {
            report = Report(db);
            report.AcceptChunk(0, Now.AddMinutes(1), true);
            report.Complete(coverage, 2, Now.AddMinutes(2));
            await db.SaveChangesAsync(ct);
        }

        await using var read = testDb.NewContext();
        var loaded = await read.ReconciliationReports.AsNoTracking().SingleAsync(r => r.Id == report.Id, ct);
        Assert.Equal(ReconciliationStatus.Completed, loaded.Status);
        Assert.Equal(report.CommandId, loaded.CommandId);
        Assert.Equal(report.RequestedById, loaded.RequestedById);
        Assert.Equal(1, loaded.ReceivedChunkCount);
        Assert.Equal(Now.AddMinutes(1), loaded.CapturedAt);
        Assert.Equal(Now.AddMinutes(2), loaded.ComparedAt);
        Assert.NotNull(loaded.Coverage);
        Assert.Equal(coverage.AreaIds, loaded.Coverage.AreaIds);
        Assert.Equal(coverage.VendorAccessLevelRefs, loaded.Coverage.VendorAccessLevelRefs);
        Assert.Equal(8, loaded.Coverage.ComparedRecordCount);
        Assert.Equal(6, loaded.Coverage.InSyncCount);
        Assert.NotEqual(0u, loaded.Version);
    }

    [Fact]
    public async Task Finding_round_trips_with_details()
    {
        var ct = TestContext.Current.CancellationToken;
        var testDb = await TenantTestDb.CreateAsync(postgres, ct);
        var details = new FindingDetails(
            ["cardNumber", "validTo"],
            new FindingValues("****4444", Now, Now.AddDays(30)),
            new FindingValues("****8888", Now, Now.AddDays(40)));
        var personId = PersonId.New();
        var areaId = AreaId.New();
        var taskId = StaffTaskId.New();
        ReconciliationFinding finding;
        await using (var db = testDb.NewContext())
        {
            var report = Report(db);
            finding = ReconciliationFinding.Create(report.Id, FindingKind.Mismatch, personId, areaId, null, FulfillmentId.New(),
                "A-1", personId.Value.ToString(), "L1", "5555666677778888", details, Now);
            finding.AttachTask(taskId);
            db.ReconciliationFindings.Add(finding);
            await db.SaveChangesAsync(ct);
        }

        await using var read = testDb.NewContext();
        var loaded = await read.ReconciliationFindings.AsNoTracking().SingleAsync(f => f.Id == finding.Id, ct);
        Assert.Equal(FindingKind.Mismatch, loaded.Kind);
        Assert.Equal(personId, loaded.PersonId);
        Assert.Equal(areaId, loaded.AreaId);
        Assert.Null(loaded.EntitlementId);
        Assert.Equal(finding.FulfillmentId, loaded.FulfillmentId);
        Assert.Equal("5555666677778888", loaded.PacsCardNumber);
        Assert.Equal(taskId, loaded.StaffTaskId);
        Assert.Equal(details.Differences, loaded.Details.Differences);
        Assert.Equal(details.Expected, loaded.Details.Expected);
        Assert.Equal(details.Actual, loaded.Details.Actual);
    }

    [Fact]
    public async Task Export_records_are_deleted_with_report()
    {
        var ct = TestContext.Current.CancellationToken;
        var testDb = await TenantTestDb.CreateAsync(postgres, ct);
        ReconciliationReport report;
        await using (var db = testDb.NewContext())
        {
            report = Report(db);
            db.ReconciliationExportRecords.AddRange(
                new ReconciliationExportRecord(report.Id, 0, 0, "AccessAssignment", "A-1", "p1", "4111222233334444", "L1", Now, null, true),
                new ReconciliationExportRecord(report.Id, 0, 1, "AccessAssignment", "A-2", "p2", null, "L1", null, Now, false));
            db.ReconciliationFindings.Add(ReconciliationFinding.Create(report.Id, FindingKind.ExtraInPacs, null, null, null, null,
                "A-1", "p1", "L1", "4111222233334444", new FindingDetails([], null, null), Now));
            await db.SaveChangesAsync(ct);
        }

        await using (var db = testDb.NewContext())
        {
            var stored = await db.ReconciliationExportRecords.AsNoTracking().Where(e => e.ReportId == report.Id)
                .OrderBy(e => e.Ordinal).ToListAsync(ct);
            Assert.Equal(2, stored.Count);
            Assert.Equal("4111222233334444", stored[0].CardNumber);
            Assert.False(stored[1].IsActive);

            await db.ReconciliationReports.Where(r => r.Id == report.Id).ExecuteDeleteAsync(ct);
        }

        await using var read = testDb.NewContext();
        Assert.False(await read.ReconciliationExportRecords.AnyAsync(e => e.ReportId == report.Id, ct));
        Assert.False(await read.ReconciliationFindings.AnyAsync(f => f.ReportId == report.Id, ct));
    }
}
