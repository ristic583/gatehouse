using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Gatehouse.Domain.Common;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Gatehouse.IntegrationTests.Scenarios.Infrastructure;

/// <summary>
/// One AppHost for the whole scenario collection: both PACS simulators, both agents and the Building.Simulator, with helpers to act as
/// demo users, read PACS state and swipe cards.
/// </summary>
public sealed class ScenarioAppHostFixture : IAsyncLifetime
{
    public static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(6);
    public static readonly TimeSpan TestTimeout = TimeSpan.FromMinutes(6);
    private static readonly string[] PacsInstances = ["HQ-Copenhagen", "Warehouse-Aarhus"];
    private readonly Dictionary<string, ScenarioClient> _clients = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _signInLock = new(1, 1);
    private DistributedApplication? _app;
    private string _demoPassword = string.Empty;

    public DistributedApplication App => _app ?? throw new InvalidOperationException("The scenario AppHost is not started.");

    public Uri WebUrl { get; private set; } = new("http://localhost:3000");

    public NpgsqlDataSource TenantDb { get; private set; } = null!;

    public NpgsqlDataSource LegacyPacsDb { get; private set; } = null!;

    public HttpClient LegacyPacsSim { get; private set; } = null!;

    public HttpClient CloudPacsSim { get; private set; } = null!;

    public HttpClient BuildingSim { get; private set; } = null!;

    public ScenarioAccess Access { get; private set; } = null!;

    public PacsProbe Pacs { get; private set; } = null!;

    public SwipeProbe Swipes { get; private set; } = null!;

    public ScenarioClock Clock { get; private set; } = null!;

    public AgentControl Agents { get; private set; } = null!;

    public ScenarioDb Db { get; private set; } = null!;

    public ScenarioMappings Mappings { get; private set; } = null!;

    public GatewayProbe Gateway { get; private set; } = null!;

    /// <summary>Per-test budget (6 min), linked to the xUnit test token.</summary>
    public static CancellationTokenSource NewTestTimeout()
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TestTimeout);
        return cts;
    }

    public async ValueTask InitializeAsync()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(StartTimeout);
        var ct = cts.Token;
        var startedAt = DateTimeOffset.UtcNow;

        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Gatehouse_AppHost>(["DcpPublisher:RandomizePorts=false"], ct);
        _app = await builder.BuildAsync(ct);
        await _app.StartAsync(ct);

        foreach (var resource in new[] { "api", "gateway", "web" })
        {
            await _app.ResourceNotifications.WaitForResourceHealthyAsync(resource, ct);
        }

        foreach (var resource in new[] { "agent-nordlys-hq", "agent-nordlys-aarhus", "building-sim" })
        {
            await _app.ResourceNotifications.WaitForResourceAsync(resource, KnownResourceStates.Running, ct);
        }

        var demoPassword = _app.Services.GetRequiredService<DistributedApplicationModel>().Resources
            .OfType<ParameterResource>().Single(p => p.Name == "demo-user-password");
        _demoPassword = await demoPassword.GetValueAsync(ct) ?? throw new InvalidOperationException("demo-user-password has no value.");

        var server = await _app.GetConnectionStringAsync("postgres", ct);
        TenantDb = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(server) { Database = new TenantId("nordlys").DatabaseName, MaxPoolSize = 4 }.ConnectionString);
        LegacyPacsDb = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(await _app.GetConnectionStringAsync("legacypacs", ct)) { MaxPoolSize = 2 }.ConnectionString);
        LegacyPacsSim = _app.CreateHttpClient("legacypacs-sim", "http");
        CloudPacsSim = _app.CreateHttpClient("cloudpacs-sim", "http");
        BuildingSim = _app.CreateHttpClient("building-sim", "http");
        WebUrl = _app.GetEndpoint("web", "http");

        Db = new ScenarioDb(TenantDb);
        Pacs = new PacsProbe(LegacyPacsSim, CloudPacsSim, LegacyPacsDb);
        Swipes = new SwipeProbe(BuildingSim);
        Clock = new ScenarioClock(TenantDb);
        Access = new ScenarioAccess(this);
        Agents = new AgentControl(this);
        Mappings = new ScenarioMappings(this);

        foreach (var name in PacsInstances)
        {
            await Db.WaitForHeartbeatAsync(name, startedAt, TimeSpan.FromMinutes(3), ct);
        }

        // Never depend on what an e2e run left behind: its resets switch nordlys to Manual fulfillment, scenarios need Connector.
        using (var reset = await (await ClientAsync(ScenarioPersonas.TenantAdmin, ct))
            .PostAsync("v1/dev/reset-demo-access?fulfillmentMode=Connector", null, ct))
        {
            reset.EnsureSuccessStatusCode();
        }

        await PacsProbe.WaitUntilAsync(async c => await SimulatorHealthyAsync(BuildingSim, c), TimeSpan.FromSeconds(60), ct);

        // After the heartbeats: the HQ agent has enrolled and written its client certificate.
        Gateway = new GatewayProbe(_app.GetEndpoint("gateway", "https"));
    }

    /// <summary>The value of an AppHost parameter (user secrets).</summary>
    public async Task<string> ParameterAsync(string name, CancellationToken ct)
    {
        var parameter = App.Services.GetRequiredService<DistributedApplicationModel>().Resources.OfType<ParameterResource>().Single(p => p.Name == name);
        return await parameter.GetValueAsync(ct) ?? throw new InvalidOperationException($"{name} has no value.");
    }

    /// <summary>Drops the cached persona sessions (e.g. after a tenant suspension ended them); the next <see cref="ClientAsync"/> signs in again.</summary>
    public async Task ForgetSessionsAsync()
    {
        await _signInLock.WaitAsync();
        try
        {
            foreach (var client in _clients.Values)
            {
                client.Dispose();
            }

            _clients.Clear();
        }
        finally
        {
            _signInLock.Release();
        }
    }

    /// <summary>A signed-in session of a persona (cached for the collection).</summary>
    public async Task<ScenarioClient> ClientAsync(string email, CancellationToken ct)
    {
        await _signInLock.WaitAsync(ct);
        try
        {
            if (_clients.TryGetValue(email, out var cached))
            {
                return cached;
            }

            var client = new ScenarioClient(WebUrl, email, _demoPassword);
            try
            {
                await client.SignInAsync(ct);
            }
            catch
            {
                client.Dispose();
                throw;
            }

            _clients[email] = client;
            return client;
        }
        finally
        {
            _signInLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients.Values)
        {
            client.Dispose();
        }

        Gateway?.Dispose();
        LegacyPacsSim?.Dispose();
        CloudPacsSim?.Dispose();
        BuildingSim?.Dispose();
        if (TenantDb is not null)
        {
            await TenantDb.DisposeAsync();
        }

        if (LegacyPacsDb is not null)
        {
            await LegacyPacsDb.DisposeAsync();
        }

        _signInLock.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    private static async Task<bool> SimulatorHealthyAsync(HttpClient sim, CancellationToken ct)
    {
        try
        {
            using var health = await sim.GetAsync(new Uri("/health", UriKind.Relative), ct);
            return health.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }
}
