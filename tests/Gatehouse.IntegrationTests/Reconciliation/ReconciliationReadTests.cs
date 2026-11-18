using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gatehouse.Application.Reconciliation.Dtos;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Adapters;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.Reconciliation;
using Gatehouse.Domain.Tasks;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Reconciliation;

public sealed class ReconciliationReadTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Tenant = "nordlys";
    private const string Instance = "HQ-Copenhagen";
    private const string FullCardNumber = "4111222233334444";

    private readonly GatehouseApiFactory _api = new(postgres);
    private PacsInstanceId _instanceId;
    private FulfillmentMode _mode;
    private string? _caps;

    public async ValueTask InitializeAsync()
    {
        await _api.InitializeAsync();
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var instance = await db.PacsInstances.AsNoTracking().SingleAsync(p => p.Name == Instance);
        (_instanceId, _mode, _caps) = (instance.Id, instance.FulfillmentMode, instance.CapabilitiesJson);
        var caps = JsonSerializer.Serialize(
            new PacsCapabilities("LegacyPacs", "1.0.0", true, false, false, false, true, [DoorCommandKind.MomentaryUnlock], 50), ConnectorJson.Options);
        var id = _instanceId;
        await db.PacsInstances.Where(p => p.Id == id).ExecuteUpdateAsync(
            s => s.SetProperty(p => p.FulfillmentMode, FulfillmentMode.Connector).SetProperty(p => p.CapabilitiesJson, caps));
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    [Fact]
    public async Task Instances_list_connector_instances_with_can_run()
    {
        var ct = TestContext.Current.CancellationToken;
        using var jonas = _api.CreateClientFor(DemoUser.Jonas);

        var instances = await jonas.GetFromJsonAsync<List<ReconciliationInstanceDto>>(new Uri("/api/v1/reconciliation/instances", UriKind.Relative), ApiJson.Options, ct);

        var hq = Assert.Single(instances!, i => i.PacsInstanceId == _instanceId.Value);
        Assert.Equal("Connector", hq.FulfillmentMode);
        Assert.True(hq.CanRun);
        Assert.Null(hq.BlockedReason);
        Assert.NotEmpty(hq.SiteNames);
    }

    [Fact]
    public async Task Tenant_admin_sees_instances_read_only()
    {
        var ct = TestContext.Current.CancellationToken;
        using var freja = _api.CreateClientFor(DemoUser.Freja);

        var instances = await freja.GetFromJsonAsync<List<ReconciliationInstanceDto>>(new Uri("/api/v1/reconciliation/instances", UriKind.Relative), ApiJson.Options, ct);

        Assert.Contains(instances!, i => i.PacsInstanceId == _instanceId.Value);
        Assert.All(instances!, i => Assert.False(i.CanRun));
    }

    [Fact]
    public async Task Reports_are_newest_first()
    {
        var ct = TestContext.Current.CancellationToken;
        var at = DateTimeOffset.UtcNow.AddMinutes(-30);
        var ids = new List<Guid>();
        await using (var scope = await _api.CreateTenantScopeAsync(Tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            for (var i = 0; i < 3; i++)
            {
                var report = ReconciliationReport.Request(_instanceId, null, CommandId.New(), at.AddMinutes(i));
                report.Fail("connector.failed", "Test", at.AddMinutes(i));
                db.ReconciliationReports.Add(report);
                ids.Add(report.Id.Value);
            }

            await db.SaveChangesAsync(ct);
        }

        using var jonas = _api.CreateClientFor(DemoUser.Jonas);
        var reports = await jonas.GetFromJsonAsync<List<ReconciliationReportSummaryDto>>(
            new Uri($"/api/v1/reconciliation/reports?pacsInstanceId={_instanceId.Value}&limit=2", UriKind.Relative), ApiJson.Options, ct);

        Assert.Equal([ids[2], ids[1]], reports!.Select(r => r.ReportId));
        Assert.All(reports!, r => Assert.Equal(Instance, r.PacsInstanceName));
    }

    [Fact]
    public async Task Report_detail_has_coverage_and_masked_findings()
    {
        var ct = TestContext.Current.CancellationToken;
        Guid reportId;
        Guid siteId;
        await using (var scope = await _api.CreateTenantScopeAsync(Tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var area = await db.Areas.AsNoTracking().SingleAsync(a => a.Code == "CPH-VAULT", ct);
            siteId = (await db.Sites.AsNoTracking().SingleAsync(s => s.Code == "CPH-HQ", ct)).Id.Value;
            var now = DateTimeOffset.UtcNow;
            var report = ReconciliationReport.Request(_instanceId, null, CommandId.New(), now);
            report.AcceptChunk(0, now, isLast: true);
            report.Complete(new ReconciliationCoverage([siteId], [area.Id.Value], ["LVL-VAULT"], ["AccessAssignment"], 3, 2, 1, 0, 1), 1, now);
            var finding = ReconciliationFinding.Create(
                report.Id, FindingKind.ExtraInPacs, null, area.Id, null, null, "pacs-1", "unknown-person", "LVL-VAULT", FullCardNumber,
                new FindingDetails([], null, new FindingValues("************4444", null, null)), now);
            var task = StaffTask.Open(StaffTaskKind.ReconciliationFinding, new Domain.Sites.SiteId(siteId), "ReconciliationFinding", finding.Id.Value, now);
            finding.AttachTask(task.Id);
            db.AddRange(report, finding, task);
            await db.SaveChangesAsync(ct);
            reportId = report.Id.Value;
        }

        using var jonas = _api.CreateClientFor(DemoUser.Jonas);
        using var response = await jonas.GetAsync(new Uri($"/api/v1/reconciliation/reports/{reportId}", UriKind.Relative), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        var dto = JsonSerializer.Deserialize<ReconciliationReportDto>(body, ApiJson.Options)!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(FullCardNumber, body, StringComparison.Ordinal);
        Assert.Equal("Completed", dto.Summary.Status);
        Assert.Equal(1, dto.Summary.FindingCount);
        var coverage = dto.Coverage!;
        Assert.Equal(siteId, Assert.Single(coverage.Sites).Id);
        Assert.Single(coverage.Areas);
        Assert.Equal(3, coverage.ExportedRecordCount);
        Assert.Equal(2, coverage.ComparedRecordCount);
        Assert.Equal(1, coverage.IgnoredInactiveCount);
        var found = Assert.Single(dto.Findings);
        Assert.Equal("ExtraInPacs", found.Kind);
        Assert.Equal("************4444", found.Actual!.CardNumberMasked);
        Assert.NotNull(found.AreaName);
        Assert.Equal("Open", found.TaskStatus);
        Assert.NotEqual(Guid.Empty, found.StaffTaskId);
    }

    [Fact]
    public async Task Unknown_report_is_404()
    {
        var ct = TestContext.Current.CancellationToken;
        using var jonas = _api.CreateClientFor(DemoUser.Jonas);

        using var response = await jonas.GetAsync(new Uri($"/api/v1/reconciliation/reports/{Guid.CreateVersion7()}", UriKind.Relative), ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("reconciliation.report_not_found", await response.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }
}
