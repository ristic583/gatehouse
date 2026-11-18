using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using Gatehouse.Application.Connectors.ReceiveRecordExport;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Adapters;
using Gatehouse.Connector.Contracts.Events;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.Reconciliation;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Connectors;

public sealed class ExportsEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Path = "/connector/v1/exports";
    private const string CardNumber = "100200300";
    private static readonly DateTimeOffset Captured = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly GatewayFactory _factory = new(postgres);
    private readonly List<ReconciliationReportId> _reports = [];

    public async ValueTask InitializeAsync()
    {
        await _factory.InitializeAsync();
        await ConnectorTestEnrollment.ResetAsync(_factory);
        await ClearOutboxAsync();
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private async Task<ReconciliationReport> NewReportAsync(string pacsName, Action<ReconciliationReport>? arrange = null)
    {
        await using var scope = await ConnectorTestEnrollment.ScopeAsync(_factory, "nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var instance = await db.PacsInstances.AsNoTracking().SingleAsync(p => p.Name == pacsName);
        var report = ReconciliationReport.Request(instance.Id, null, CommandId.New(), Captured);
        arrange?.Invoke(report);
        db.ReconciliationReports.Add(report);
        await db.SaveChangesAsync();
        _reports.Add(report.Id);
        return report;
    }

    private static RecordExport Chunk(ReconciliationReport report, int index, bool isLast, int records = 2) => new(
        report.Id.Value,
        report.CommandId.Value,
        index,
        isLast,
        Captured,
        Enumerable.Range(0, records).Select(i => new PacsRecord(
            RecordType.AccessAssignment, $"assign-{index}-{i}", Guid.NewGuid().ToString(), CardNumber, "AL-CPH-SERVER", Captured.AddDays(-1), Captured.AddDays(10), true)).ToArray());

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, RecordExport chunk, X509Certificate2 cert) =>
        ConnectorTestEnrollment.PostAsync(client, Path, chunk, cert);

    [Fact]
    public async Task Chunks_in_order_are_staged_and_last_publishes_received()
    {
        var ct = TestContext.Current.CancellationToken;
        using var cert = await ConnectorTestEnrollment.EnrollAsync(_factory, "nordlys", "HQ-Copenhagen");
        using var client = _factory.CreateClient();
        var report = await NewReportAsync("HQ-Copenhagen");

        using var first = await PostAsync(client, Chunk(report, 0, false, 3), cert);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(new RecordExportResponse(true, false), await first.Content.ReadFromJsonAsync<RecordExportResponse>(ConnectorJson.Options, ct));
        Assert.Empty(await OutboxPayloadsAsync(ct));

        using var last = await PostAsync(client, Chunk(report, 1, true, 2), cert);
        Assert.Equal(HttpStatusCode.Accepted, last.StatusCode);

        await using var scope = await ConnectorTestEnrollment.ScopeAsync(_factory, "nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var stored = await db.ReconciliationReports.AsNoTracking().SingleAsync(r => r.Id == report.Id, ct);
        Assert.Equal(ReconciliationStatus.Comparing, stored.Status);
        Assert.Equal(2, stored.ReceivedChunkCount);
        Assert.Equal(Captured, stored.CapturedAt);
        var rows = await db.ReconciliationExportRecords.AsNoTracking().Where(e => e.ReportId == report.Id)
            .OrderBy(e => e.ChunkIndex).ThenBy(e => e.Ordinal).ToListAsync(ct);
        Assert.Equal(5, rows.Count);
        Assert.Equal([0, 1, 2, 0, 1], rows.Select(r => r.Ordinal).ToArray());
        Assert.Equal("AccessAssignment", rows[0].RecordType);
        Assert.Equal(CardNumber, rows[0].CardNumber);
        var payload = Assert.Single(await OutboxPayloadsAsync(ct));
        using var json = System.Text.Json.JsonDocument.Parse(payload);
        Assert.Equal(report.Id.Value, json.RootElement.GetProperty("exportId").GetGuid());
        Assert.Equal(5, json.RootElement.GetProperty("recordCount").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("chunkCount").GetInt32());
        Assert.DoesNotContain(CardNumber, payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Duplicate_chunk_is_202_and_ignored()
    {
        var ct = TestContext.Current.CancellationToken;
        using var cert = await ConnectorTestEnrollment.EnrollAsync(_factory, "nordlys", "HQ-Copenhagen");
        using var client = _factory.CreateClient();
        var report = await NewReportAsync("HQ-Copenhagen");

        using var first = await PostAsync(client, Chunk(report, 0, false), cert);
        using var again = await PostAsync(client, Chunk(report, 0, false), cert);

        Assert.Equal(HttpStatusCode.Accepted, again.StatusCode);
        Assert.Equal(new RecordExportResponse(false, true), await again.Content.ReadFromJsonAsync<RecordExportResponse>(ConnectorJson.Options, ct));
        await using var scope = await ConnectorTestEnrollment.ScopeAsync(_factory, "nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        Assert.Equal(2, await db.ReconciliationExportRecords.CountAsync(e => e.ReportId == report.Id, ct));
        Assert.Equal(1, (await db.ReconciliationReports.AsNoTracking().SingleAsync(r => r.Id == report.Id, ct)).ReceivedChunkCount);
    }

    [Fact]
    public async Task Skipped_chunk_is_409_out_of_order()
    {
        var ct = TestContext.Current.CancellationToken;
        using var cert = await ConnectorTestEnrollment.EnrollAsync(_factory, "nordlys", "HQ-Copenhagen");
        using var client = _factory.CreateClient();
        var report = await NewReportAsync("HQ-Copenhagen");

        using var response = await PostAsync(client, Chunk(report, 1, false), cert);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("connector.export_chunk_out_of_order", await response.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_of_other_instance_is_404()
    {
        var ct = TestContext.Current.CancellationToken;
        using var cert = await ConnectorTestEnrollment.EnrollAsync(_factory, "nordlys", "HQ-Copenhagen");
        using var client = _factory.CreateClient();
        var report = await NewReportAsync("Warehouse-Aarhus");

        using var response = await PostAsync(client, Chunk(report, 0, true), cert);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("connector.export_not_found", await response.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failed_report_is_409_closed()
    {
        var ct = TestContext.Current.CancellationToken;
        using var cert = await ConnectorTestEnrollment.EnrollAsync(_factory, "nordlys", "HQ-Copenhagen");
        using var client = _factory.CreateClient();
        var report = await NewReportAsync("HQ-Copenhagen", r => r.Fail("reconciliation.timed_out", "Timed out.", Captured));

        using var response = await PostAsync(client, Chunk(report, 0, true), cert);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("connector.export_closed", await response.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Too_many_records_is_413()
    {
        var ct = TestContext.Current.CancellationToken;
        using var cert = await ConnectorTestEnrollment.EnrollAsync(_factory, "nordlys", "HQ-Copenhagen");
        using var client = _factory.CreateClient();
        var report = await NewReportAsync("HQ-Copenhagen");

        using var response = await PostAsync(client, Chunk(report, 0, true, 1001), cert);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Contains("connector.payload_too_large", await response.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }

    private async Task<List<string>> OutboxPayloadsAsync(CancellationToken ct)
    {
        await using var scope = await ConnectorTestEnrollment.ScopeAsync(_factory, "nordlys");
        return await scope.ServiceProvider.GetRequiredService<TenantDbContext>().OutboxMessages.AsNoTracking()
            .Where(m => m.Type == "RecordExportReceived").Select(m => m.Payload).ToListAsync(ct);
    }

    private async Task ClearOutboxAsync()
    {
        await using var scope = await ConnectorTestEnrollment.ScopeAsync(_factory, "nordlys");
        await scope.ServiceProvider.GetRequiredService<TenantDbContext>().OutboxMessages.ExecuteDeleteAsync(CancellationToken.None);
    }
}
