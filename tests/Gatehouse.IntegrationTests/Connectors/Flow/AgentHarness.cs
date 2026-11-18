using System.Security.Cryptography.X509Certificates;
using Gatehouse.Agent.Execution;
using Gatehouse.Agent.Storage;
using Gatehouse.Agent.Transport;
using Gatehouse.Connector.Contracts.Adapters;
using Gatehouse.Connector.Contracts.Events;
using Gatehouse.Connector.Contracts.Protocol;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gatehouse.IntegrationTests.Connectors.Flow;

/// <summary>Real CommandExecutor + PollLoop + GatewayClient talking to the in-process gateway; one iteration at a time.</summary>
internal sealed class AgentHarness : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gh-flow-" + Guid.NewGuid().ToString("N"));
    private readonly HttpClient _http;
    private readonly DropAcksHandler _handler;

    public AgentHarness(GatewayFactory gateway, X509Certificate2 cert, IPacsAdapter adapter)
    {
        PacsAdapter = adapter;
        _handler = new DropAcksHandler(cert);
        _http = gateway.CreateDefaultClient(_handler);
        Client = new GatewayClient(_http, "1.0.0-test");
        var db = new AgentDatabase(_dir);
        Pending = new PendingAckStore(db);
        Executor = new CommandExecutor(adapter, new ProcessedCommandStore(db), new GatewayExportSink(Client, TimeProvider.System, NullLogger<GatewayExportSink>.Instance), TimeProvider.System, NullLogger<CommandExecutor>.Instance);
        Loop = new PollLoop(Client, Executor, Pending, TimeProvider.System, NullLogger<PollLoop>.Instance, waitSeconds: 0);
        Events = new PendingEventStore(db);
        Send = new EventSendLoop(Client, Events, TimeProvider.System, NullLogger<EventSendLoop>.Instance);
        Pump = new EventPumpLoop(adapter, Events, Send.Notify, TimeProvider.System, NullLogger<EventPumpLoop>.Instance);
    }

    public IPacsAdapter PacsAdapter { get; }

    /// <summary>The fake adapter, for tests that built the harness with one.</summary>
    public FakePacsAdapter Adapter => (FakePacsAdapter)PacsAdapter;

    public GatewayClient Client { get; }

    public PendingAckStore Pending { get; }

    public CommandExecutor Executor { get; }

    public PollLoop Loop { get; }

    public PendingEventStore Events { get; }

    public EventPumpLoop Pump { get; }

    public EventSendLoop Send { get; }

    /// <summary>While true, ack requests fail before reaching the gateway (network loss).</summary>
    public bool DropAcks { get => _handler.DropAcks; set => _handler.DropAcks = value; }

    public async Task RunCyclesAsync(int cycles)
    {
        for (var i = 0; i < cycles; i++)
        {
            await Loop.RunOnceAsync(TestContext.Current.CancellationToken);
        }
    }

    public Task HeartbeatAsync() => Client.HeartbeatAsync(ConnectorTestEnrollment.NewHeartbeat(), TestContext.Current.CancellationToken);

    public void Dispose()
    {
        _http.Dispose();
        _handler.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    private sealed class DropAcksHandler(X509Certificate2 cert) : DelegatingHandler
    {
        public bool DropAcks { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (DropAcks && request.RequestUri!.AbsolutePath.EndsWith("/acks", StringComparison.Ordinal))
            {
                throw new HttpRequestException("simulated network loss");
            }

            request.Headers.Add(TestClientCertificateStartupFilter.Header, Convert.ToBase64String(cert.RawData));
            return base.SendAsync(request, cancellationToken);
        }
    }
}
