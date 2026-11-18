using System.Text.Json;
using Gatehouse.Application.Reconciliation.Scheduling;
using Gatehouse.Application.Tenancy;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Adapters;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.Reconciliation;
using Gatehouse.Domain.Sites;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Gatehouse.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Gatehouse.IntegrationTests.Reconciliation;

/// <summary>
/// The monthly scheduler and report timeouts. Each test owns its PACS instances; reports the scheduler requests for other
/// connector instances (any tenant) during a test are removed again.
/// </summary>
public sealed class ReconciliationScheduleTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Tenant = "nordlys";

    private readonly GatehouseApiFactory _api = new(postgres);
    private readonly List<PacsInstanceId> _instances = [];
    private readonly DateTimeOffset _start = DateTimeOffset.UtcNow.AddSeconds(-1);
    private SiteId _siteId;
    private FloorId _floorId;
    private SecurityLevelId _levelId;

    public async ValueTask InitializeAsync()
    {
        await _api.InitializeAsync();
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var (site, floor, level) = MappingData.Building(db);
        await db.SaveChangesAsync();
        (_siteId, _floorId, _levelId) = (site.Id, floor.Id, level.Id);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    [Fact]
    public async Task Due_instance_gets_a_report()
    {
        var ct = TestContext.Current.CancellationToken;
        var instanceId = await InstanceAsync(FulfillmentMode.Connector, ct);
        await ReportAsync(instanceId, DateTimeOffset.UtcNow.AddDays(-31), running: false, ct);

        var result = await RunAsync(DateTimeOffset.UtcNow, ct);

        Assert.True(result[new TenantId(Tenant)].Requested >= 1);
        var reports = await ReportsAsync(instanceId, ct);
        var report = Assert.Single(reports, r => r.Status == ReconciliationStatus.Requested);
        Assert.Null(report.RequestedById);
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        Assert.True(await db.ConnectorCommands.AnyAsync(c => c.Id == report.CommandId && c.Type == "ExportRecords", ct));
        var audit = await db.AuditEntries.AsNoTracking()
            .SingleAsync(a => a.Action == "reconciliation.requested" && a.SubjectId == report.Id.Value.ToString(), ct);
        Assert.Equal("reconciliation-scheduler", audit.ActorId);
    }

    [Fact]
    public async Task Recent_report_is_not_repeated()
    {
        var ct = TestContext.Current.CancellationToken;
        var instanceId = await InstanceAsync(FulfillmentMode.Connector, ct);
        await ReportAsync(instanceId, DateTimeOffset.UtcNow.AddDays(-5), running: false, ct);

        await RunAsync(DateTimeOffset.UtcNow, ct);

        Assert.Single(await ReportsAsync(instanceId, ct));
    }

    [Fact]
    public async Task Manual_instance_is_skipped()
    {
        var ct = TestContext.Current.CancellationToken;
        var instanceId = await InstanceAsync(FulfillmentMode.Manual, ct);

        await RunAsync(DateTimeOffset.UtcNow, ct);

        Assert.Empty(await ReportsAsync(instanceId, ct));
    }

    [Fact]
    public async Task Stuck_report_times_out()
    {
        var ct = TestContext.Current.CancellationToken;
        var instanceId = await InstanceAsync(FulfillmentMode.Connector, ct);
        var requestedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var stuckId = await ReportAsync(instanceId, requestedAt, running: true, ct);

        var early = await RunAsync(requestedAt.AddHours(5), ct);
        var statusEarly = (await ReportsAsync(instanceId, ct)).Single(r => r.Id == stuckId).Status;
        var late = await RunAsync(requestedAt.AddHours(7), ct);

        Assert.Equal(ReconciliationStatus.Requested, statusEarly);
        Assert.True(late[new TenantId(Tenant)].TimedOut >= 1);
        var stuck = (await ReportsAsync(instanceId, ct)).Single(r => r.Id == stuckId);
        Assert.Equal(ReconciliationStatus.Failed, stuck.Status);
        Assert.Equal("reconciliation.timed_out", stuck.FailureCode);
        Assert.Equal(0, early[new TenantId(Tenant)].TimedOut);
    }

    private async Task<IReadOnlyDictionary<TenantId, ReconciliationScheduleResult>> RunAsync(DateTimeOffset now, CancellationToken ct)
    {
        var runner = new ReconciliationScheduleRunner(
            _api.Services.GetRequiredService<IServiceScopeFactory>(),
            _api.Services.GetRequiredService<ITenantScopeFactory>(),
            _api.Services.GetRequiredService<ITenantLoop>(),
            new FakeTimeProvider(now),
            Options.Create(new ReconciliationScheduleOptions()),
            NullLogger<ReconciliationScheduleRunner>.Instance);
        return await runner.RunOnceAsync(ct);
    }

    private async Task<PacsInstanceId> InstanceAsync(FulfillmentMode mode, CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var instance = PacsInstance.Create("Sched " + Guid.NewGuid().ToString("N")[..6], PacsVendor.LegacyPacs, [_siteId]).Value;
        instance.SetFulfillmentMode(mode);
        instance.RecordHeartbeat(DateTimeOffset.UtcNow, "1.0.0", JsonSerializer.Serialize(
            new PacsCapabilities("LegacyPacs", "1.0.0", true, false, false, false, true, [DoorCommandKind.MomentaryUnlock], 50), ConnectorJson.Options));
        db.Add(instance);
        await db.SaveChangesAsync(ct);
        _instances.Add(instance.Id);
        return instance.Id;
    }

    private async Task<ReconciliationReportId> ReportAsync(PacsInstanceId instanceId, DateTimeOffset requestedAt, bool running, CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var report = ReconciliationReport.Request(instanceId, null, CommandId.New(), requestedAt);
        if (!running)
        {
            report.Fail("connector.failed", "Test fixture", requestedAt);
        }

        report.ClearDomainEvents();
        db.Add(report);
        await db.SaveChangesAsync(ct);
        return report.Id;
    }

    private async Task<List<ReconciliationReport>> ReportsAsync(PacsInstanceId instanceId, CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        return await db.ReconciliationReports.AsNoTracking().Where(r => r.PacsInstanceId == instanceId).ToListAsync(ct);
    }
}
