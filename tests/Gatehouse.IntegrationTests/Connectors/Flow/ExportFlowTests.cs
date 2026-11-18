using Gatehouse.Application.Connectors;
using Gatehouse.Connector.Contracts.Adapters;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.Reconciliation;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Connectors.Flow;

public sealed class ExportFlowTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatewayFactory _gateway = new(postgres);
    private ReconciliationReportId? _reportId;
    private AgentHarness? _agent;

    public async ValueTask InitializeAsync()
    {
        await _gateway.InitializeAsync();
        await ConnectorTestEnrollment.ResetAsync(_gateway);
    }

    public async ValueTask DisposeAsync()
    {
        _agent?.Dispose();
        await _gateway.DisposeAsync();
    }

    [Fact]
    public async Task Agent_export_is_staged_and_command_acked()
    {
        var ct = TestContext.Current.CancellationToken;
        using var cert = await ConnectorTestEnrollment.EnrollAsync(_gateway, "nordlys", "HQ-Copenhagen");
        var adapter = new FakePacsAdapter();
        for (var i = 0; i < 2500; i++)
        {
            adapter.ExportedRecords.Add(new PacsRecord(RecordType.AccessAssignment, $"assign-{i}", Guid.NewGuid().ToString(), "100200300", "AL-CPH-SERVER", null, null, true));
        }

        _agent = new AgentHarness(_gateway, cert, adapter);
        CommandId commandId;
        await using (var scope = await ConnectorTestEnrollment.ScopeAsync(_gateway, "nordlys"))
        {
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<TenantDbContext>();
            var instance = await db.PacsInstances.AsNoTracking().SingleAsync(p => p.Name == "HQ-Copenhagen", ct);
            var reportId = ReconciliationReportId.New();
            var factory = new ConnectorCommandFactory(sp.GetRequiredService<IConnectorSequencer>(), TimeProvider.System);
            var command = await factory.CreateForExportAsync(instance.Id, reportId, ["AL-CPH-SERVER"], ct);
            db.ConnectorCommands.Add(command);
            db.ReconciliationReports.Add(ReconciliationReport.Request(reportId, instance.Id, null, command.Id, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(ct);
            (_reportId, commandId) = (reportId, command.Id);
        }

        await _agent.RunCyclesAsync(3);

        await using var read = await ConnectorTestEnrollment.ScopeAsync(_gateway, "nordlys");
        var rdb = read.ServiceProvider.GetRequiredService<TenantDbContext>();
        var id = _reportId.Value;
        var report = await rdb.ReconciliationReports.AsNoTracking().SingleAsync(r => r.Id == id, ct);
        Assert.Equal(ReconciliationStatus.Comparing, report.Status);
        Assert.Equal(3, report.ReceivedChunkCount);
        Assert.Equal(2500, await rdb.ReconciliationExportRecords.CountAsync(e => e.ReportId == id, ct));
        Assert.Equal(3, await rdb.ReconciliationExportRecords.Where(e => e.ReportId == id).Select(e => e.ChunkIndex).Distinct().CountAsync(ct));
        var stored = await rdb.ConnectorCommands.AsNoTracking().SingleAsync(c => c.Id == commandId, ct);
        Assert.Equal(ConnectorCommandStatus.Acked, stored.Status);
        Assert.True(await rdb.OutboxMessages.AnyAsync(m => m.Type == "RecordExportReceived", ct));
    }
}
