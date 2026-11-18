using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gatehouse.Application.Reconciliation.Dtos;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Adapters;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.Reconciliation;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Reconciliation;

public sealed class RequestReconciliationTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Tenant = "nordlys";
    private const string Instance = "HQ-Copenhagen";
    private static readonly Uri Endpoint = new("/api/v1/reconciliation/reports", UriKind.Relative);

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
        await SetInstanceAsync(FulfillmentMode.Connector, Caps(recordExport: true));
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    private static string Caps(bool recordExport) => JsonSerializer.Serialize(
        new PacsCapabilities("LegacyPacs", "1.0.0", true, false, false, false, recordExport, [DoorCommandKind.MomentaryUnlock], 50), ConnectorJson.Options);

    private async Task SetInstanceAsync(FulfillmentMode mode, string? caps)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var id = _instanceId;
        await db.PacsInstances.Where(p => p.Id == id).ExecuteUpdateAsync(
            s => s.SetProperty(p => p.FulfillmentMode, mode).SetProperty(p => p.CapabilitiesJson, caps), CancellationToken.None);
    }

    private static Task<HttpResponseMessage> RequestAsync(HttpClient client, Guid instanceId, CancellationToken ct) =>
        client.PostAsJsonAsync(Endpoint, new { pacsInstanceId = instanceId }, ct);

    [Fact]
    public async Task Request_queues_export_command_and_report()
    {
        var ct = TestContext.Current.CancellationToken;
        using var jonas = _api.CreateClientFor(DemoUser.Jonas);

        using var response = await RequestAsync(jonas, _instanceId.Value, ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<ReconciliationReportSummaryDto>(ApiJson.Options, ct);
        Assert.NotNull(dto);
        Assert.Equal("Requested", dto.Status);
        Assert.Equal(Instance, dto.PacsInstanceName);
        Assert.NotNull(dto.RequestedBy);
        Assert.Null(dto.FindingCount);

        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var report = await db.ReconciliationReports.AsNoTracking().SingleAsync(r => r.Id == new ReconciliationReportId(dto.ReportId), ct);
        var command = await db.ConnectorCommands.AsNoTracking().SingleAsync(c => c.Id == report.CommandId, ct);
        Assert.Equal("ExportRecords", command.Type);
        Assert.Equal($"export:{dto.ReportId}", command.PartitionKey);
        Assert.Equal($"ExportRecords:{dto.ReportId}:1", command.IdempotencyKey);
        Assert.Equal(CommandPriority.Normal, command.Priority);
        Assert.Null(command.FulfillmentId);
        Assert.Contains("AccessAssignment", command.PayloadJson, StringComparison.Ordinal);
        Assert.Contains(dto.ReportId.ToString(), command.PayloadJson, StringComparison.Ordinal);
        var id = _instanceId;
        var levels = await db.AccessLevelMappings.AsNoTracking().Where(m => m.PacsInstanceId == id).Select(m => m.VendorAccessLevelRef).ToListAsync(ct);
        Assert.NotEmpty(levels);
        Assert.All(levels, l => Assert.Contains(l, command.PayloadJson, StringComparison.Ordinal));
        Assert.True(await db.AuditEntries.AnyAsync(a => a.Action == "reconciliation.requested" && a.SubjectId == dto.ReportId.ToString(), ct));
    }

    [Fact]
    public async Task Manual_instance_is_409_connector_required()
    {
        var ct = TestContext.Current.CancellationToken;
        await SetInstanceAsync(FulfillmentMode.Manual, Caps(recordExport: true));
        using var jonas = _api.CreateClientFor(DemoUser.Jonas);

        using var response = await RequestAsync(jonas, _instanceId.Value, ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("reconciliation.connector_required", await response.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_unsupported_is_409()
    {
        var ct = TestContext.Current.CancellationToken;
        await SetInstanceAsync(FulfillmentMode.Connector, Caps(recordExport: false));
        using var jonas = _api.CreateClientFor(DemoUser.Jonas);

        using var response = await RequestAsync(jonas, _instanceId.Value, ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("reconciliation.export_unsupported", await response.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Second_request_while_running_is_409()
    {
        var ct = TestContext.Current.CancellationToken;
        using var jonas = _api.CreateClientFor(DemoUser.Jonas);

        using var first = await RequestAsync(jonas, _instanceId.Value, ct);
        using var second = await RequestAsync(jonas, _instanceId.Value, ct);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("reconciliation.already_running", await second.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tenant_admin_gets_403()
    {
        var ct = TestContext.Current.CancellationToken;
        using var freja = _api.CreateClientFor(DemoUser.Freja);

        using var response = await RequestAsync(freja, _instanceId.Value, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_instance_is_404()
    {
        var ct = TestContext.Current.CancellationToken;
        using var jonas = _api.CreateClientFor(DemoUser.Jonas);

        using var response = await RequestAsync(jonas, Guid.CreateVersion7(), ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("reconciliation.instance_not_found", await response.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }
}
