using System.Net.Http.Json;
using System.Text.Json;
using CloudPacs.Simulator;
using Gatehouse.Adapters.CloudPacs;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Commands;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gatehouse.IntegrationTests.Connectors.Flow;

public sealed class CloudPacsAgentFlowTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Tenant = "nordlys";
    private const string PacsName = "Warehouse-Aarhus";

    private readonly GatehouseApiFactory _api = new(postgres);
    private readonly HttpClient _adapterHttp = new();
    private readonly List<CommandId> _createdCommands = [];
    private GatewayFactory _gateway = null!;
    private WebApplication _sim = null!;
    private HttpClient _simHttp = null!;
    private CloudPacsAdapter _adapter = null!;
    private AgentHarness? _agent;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _api.InitializeAsync();
        _gateway = new GatewayFactory(postgres, _api.CatalogConnectionString);
        await _gateway.InitializeAsync();
        _sim = CloudPacsSimulatorProgram.CreateApp(["--urls=http://127.0.0.1:0"]);
        await _sim.StartAsync(Ct);
        var simUrl = new Uri(_sim.Urls.First() + "/");
        _simHttp = new HttpClient { BaseAddress = simUrl };
        _adapter = new CloudPacsAdapter(new CloudPacsAdapterOptions(simUrl, CloudPacsOptions.DefaultApiKey, "http://127.0.0.1:0"),
            _adapterHttp, NullLogger<CloudPacsAdapter>.Instance);
        await SetAarhusModeAsync(FulfillmentMode.Connector);
        var cert = await ConnectorTestEnrollment.EnrollAsync(_gateway, Tenant, PacsName);
        _agent = new AgentHarness(_gateway, cert, _adapter);
    }

    public async ValueTask DisposeAsync()
    {
        _agent?.Dispose();
        await _adapter.DisposeAsync();
        _adapterHttp.Dispose();
        _simHttp.Dispose();
        await _sim.StopAsync(CancellationToken.None);
        await _sim.DisposeAsync();
        await _gateway.DisposeAsync();
        await _api.DisposeAsync();
    }

    [Fact]
    public async Task Grant_is_applied_through_cloudpacs()
    {
        var person = "P-CLOUD-" + Guid.NewGuid().ToString("N")[..8];
        var payload = new GrantAccess(person, "4000000555", "AG-AAR-DOCK", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7));
        await QueueAsync(ConnectorCommandTypes.UpsertCardholder, person, 1,
            JsonSerializer.Serialize(new UpsertCardholder(person, "Cloud", "Tester", "4000000555", null), ConnectorJson.Options));
        var commandId = await QueueAsync(ConnectorCommandTypes.GrantAccess, person, 2, JsonSerializer.Serialize(payload, ConnectorJson.Options));

        await _agent!.HeartbeatAsync();
        await _agent.RunCyclesAsync(3);

        await using (var scope = await ConnectorTestEnrollment.ScopeAsync(_gateway, Tenant))
        {
            var command = await scope.ServiceProvider.GetRequiredService<TenantDbContext>().ConnectorCommands.AsNoTracking()
                .SingleAsync(c => c.Id == commandId, Ct);
            Assert.Equal(ConnectorCommandStatus.Acked, command.Status);
            Assert.False(string.IsNullOrEmpty(command.PacsRef));
        }

        var assignments = await _simHttp.GetFromJsonAsync<JsonElement>("dev/assignments", Ct);
        var membership = Assert.Single(assignments.EnumerateArray(), a => a.GetProperty("externalRef").GetString() == person);
        Assert.Equal("AG-AAR-DOCK", membership.GetProperty("groupId").GetString());
        Assert.True(membership.GetProperty("active").GetBoolean());
    }

    [Fact]
    public async Task Webhook_event_reaches_platform()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        var pump = PumpUntilCancelledAsync(cts.Token);

        using var emitted = await _simHttp.PostAsJsonAsync("dev/doors/D-AAR-DOCK/events", new { type = "door.forced", cardNumber = "4000000777" }, Ct);
        emitted.EnsureSuccessStatusCode();
        var eventId = (await emitted.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        while (await _agent!.Events.CountAsync(Ct) == 0 && !cts.IsCancellationRequested)
        {
            await Task.Delay(50, Ct);
        }

        await cts.CancelAsync();
        await pump;
        Assert.Equal(1, await _agent.Events.CountAsync(Ct));
        await _agent.Send.RunOnceAsync(Ct);

        Assert.Equal(0, await _agent.Events.CountAsync(Ct));
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var payloads = await scope.ServiceProvider.GetRequiredService<TenantDbContext>().OutboxMessages.AsNoTracking()
            .Where(m => m.Type == "DoorEventsReceived").Select(m => m.Payload).ToListAsync(Ct);
        var received = payloads
            .SelectMany(p => JsonDocument.Parse(p).RootElement.GetProperty("events").EnumerateArray())
            .Where(e => e.GetProperty("eventId").GetGuid() == eventId)
            .ToList();
        var ev = Assert.Single(received);
        Assert.Equal("D-AAR-DOCK", ev.GetProperty("pacsDoorRef").GetString());
    }

    private async Task PumpUntilCancelledAsync(CancellationToken ct)
    {
        try
        {
            await _agent!.Pump.PumpOnceAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // stopped by the test once the event was stored
        }
    }

    private async Task<CommandId> QueueAsync(string type, string partition, long sequence, string payloadJson)
    {
        await using var scope = await ConnectorTestEnrollment.ScopeAsync(_gateway, Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var pacs = await db.PacsInstances.AsNoTracking().SingleAsync(p => p.Name == PacsName, Ct);
        var command = ConnectorCommand.Create(pacs.Id, type, partition, sequence, CommandPriority.Normal, $"{type}:{partition}", payloadJson, null, DateTimeOffset.UtcNow.AddMilliseconds(sequence));
        db.ConnectorCommands.Add(command);
        await db.SaveChangesAsync(Ct);
        _createdCommands.Add(command.Id);
        return command.Id;
    }

    private async Task SetAarhusModeAsync(FulfillmentMode mode)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var instance = await db.PacsInstances.SingleAsync(p => p.Name == PacsName, Ct);
        instance.SetFulfillmentMode(mode);
        await db.SaveChangesAsync(Ct);
    }
}
