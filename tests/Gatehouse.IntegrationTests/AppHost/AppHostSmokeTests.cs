using System.Net;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace Gatehouse.IntegrationTests.AppHost;

[Trait("Category", "AppHost")]
[Collection(AppHostCollection.Name)]
public sealed class AppHostSmokeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    [Fact]
    public async Task Api_health_endpoint_returns_200()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(Timeout);

        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Gatehouse_AppHost>(
            ["DcpPublisher:RandomizePorts=false"], cts.Token);
        await using var app = await builder.BuildAsync(cts.Token);
        await app.StartAsync(cts.Token);

        await app.ResourceNotifications.WaitForResourceHealthyAsync("api", cts.Token);

        using var client = app.CreateHttpClient("api");
        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Agent_heartbeat_marks_pacs_instance_online()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromMinutes(5));

        var startedAt = DateTimeOffset.UtcNow;
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Gatehouse_AppHost>(
            ["DcpPublisher:RandomizePorts=false"], cts.Token);
        await using var app = await builder.BuildAsync(cts.Token);
        await app.StartAsync(cts.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("gateway", cts.Token);
        await app.ResourceNotifications.WaitForResourceAsync("agent-nordlys-hq", KnownResourceStates.Running, cts.Token);

        var server = await app.GetConnectionStringAsync("postgres", cts.Token);
        var connection = new Npgsql.NpgsqlConnectionStringBuilder(server) { Database = new Gatehouse.Domain.Common.TenantId("nordlys").DatabaseName }.ConnectionString;
        await using var source = Npgsql.NpgsqlDataSource.Create(connection);
        var deadline = DateTime.UtcNow.AddSeconds(60);
        object? heartbeat = null;
        while (heartbeat is null or DBNull && DateTime.UtcNow < deadline)
        {
            await using var command = source.CreateCommand("SELECT last_heartbeat_at FROM pacs_instances WHERE name = 'HQ-Copenhagen' AND last_heartbeat_at >= $1");
            command.Parameters.AddWithValue(startedAt);
            heartbeat = await command.ExecuteScalarAsync(cts.Token);
            if (heartbeat is null or DBNull)
            {
                await Task.Delay(1000, cts.Token);
            }
        }

        Assert.False(heartbeat is null or DBNull, "HQ-Copenhagen sent no heartbeat in this run within 60 s");
    }

    [Fact]
    public async Task Cloud_agent_heartbeat_marks_warehouse_online()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromMinutes(5));

        var startedAt = DateTimeOffset.UtcNow;
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Gatehouse_AppHost>(
            ["DcpPublisher:RandomizePorts=false"], cts.Token);
        await using var app = await builder.BuildAsync(cts.Token);
        await app.StartAsync(cts.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("gateway", cts.Token);
        await app.ResourceNotifications.WaitForResourceAsync("agent-nordlys-aarhus", KnownResourceStates.Running, cts.Token);

        var server = await app.GetConnectionStringAsync("postgres", cts.Token);
        var connection = new Npgsql.NpgsqlConnectionStringBuilder(server) { Database = new Gatehouse.Domain.Common.TenantId("nordlys").DatabaseName }.ConnectionString;
        await using var source = Npgsql.NpgsqlDataSource.Create(connection);
        var deadline = DateTime.UtcNow.AddSeconds(90);
        string? capabilities = null;
        while (capabilities is null && DateTime.UtcNow < deadline)
        {
            await using var command = source.CreateCommand(
                "SELECT capabilities::text FROM pacs_instances WHERE name = 'Warehouse-Aarhus' AND last_heartbeat_at >= $1");
            command.Parameters.AddWithValue(startedAt);
            capabilities = await command.ExecuteScalarAsync(cts.Token) as string;
            if (capabilities is null)
            {
                await Task.Delay(1000, cts.Token);
            }
        }

        Assert.False(capabilities is null, "Warehouse-Aarhus sent no heartbeat in this run within 90 s");
        using var json = System.Text.Json.JsonDocument.Parse(capabilities);
        var nativeValidity = json.RootElement.EnumerateObject().Single(p => string.Equals(p.Name, "nativeValidity", StringComparison.OrdinalIgnoreCase));
        Assert.False(nativeValidity.Value.GetBoolean());
    }

    [Fact]
    public async Task Swipe_reaches_door_state()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromMinutes(5));

        var startedAt = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Gatehouse_AppHost>(
            ["DcpPublisher:RandomizePorts=false"], cts.Token);
        await using var app = await builder.BuildAsync(cts.Token);
        await app.StartAsync(cts.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("workers", cts.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("gateway", cts.Token);
        await app.ResourceNotifications.WaitForResourceAsync("agent-nordlys-hq", KnownResourceStates.Running, cts.Token);
        await app.ResourceNotifications.WaitForResourceAsync("building-sim", KnownResourceStates.Running, cts.Token);

        var server = await app.GetConnectionStringAsync("postgres", cts.Token);
        var connection = new Npgsql.NpgsqlConnectionStringBuilder(server) { Database = new Gatehouse.Domain.Common.TenantId("nordlys").DatabaseName }.ConnectionString;
        await using var source = Npgsql.NpgsqlDataSource.Create(connection);
        // The AppHost database is a persistent volume: a heartbeat from an earlier run does not mean this run's agent reaches the gateway.
        var fresh = await WaitUntilAsync(source, $"SELECT 1 FROM pacs_instances WHERE name = 'HQ-Copenhagen' AND last_heartbeat_at >= '{startedAt}'::timestamptz", TimeSpan.FromSeconds(90), cts.Token);
        Assert.True(fresh, "HQ-Copenhagen sent no heartbeat in this run within 90 s");

        var card = "SMOKE-" + Guid.NewGuid().ToString("N")[..8];
        using var sim = app.CreateHttpClient("building-sim", "http");
        await WaitForSimulatorAsync(sim, cts.Token);
        var swipedAt = DateTimeOffset.UtcNow.AddSeconds(-2).ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        using var swipe = await sim.PostAsync(
            new Uri("/swipes", UriKind.Relative),
            System.Net.Http.Json.JsonContent.Create(new { doorRef = "D-CPH-LOBBY", cardNumber = card }),
            cts.Token);
        Assert.Equal(HttpStatusCode.OK, swipe.StatusCode);

        var found = await WaitUntilAsync(
            source, $"SELECT 1 FROM door_events WHERE type = 'AccessDenied' AND pacs_door_ref = 'D-CPH-LOBBY' AND received_at >= '{swipedAt}'::timestamptz", TimeSpan.FromSeconds(10), cts.Token);
        Assert.True(found, "No AccessDenied door_events row within 10 s of the swipe");
    }

    private static async Task WaitForSimulatorAsync(HttpClient sim, CancellationToken ct)
    {
        for (var i = 0; i < 60; i++)
        {
            try
            {
                using var health = await sim.GetAsync(new Uri("/health", UriKind.Relative), ct);
                if (health.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }

            await Task.Delay(500, ct);
        }
    }

    private static async Task<bool> WaitUntilAsync(Npgsql.NpgsqlDataSource source, string sql, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await using var command = source.CreateCommand(sql);
            if (await command.ExecuteScalarAsync(ct) is not (null or DBNull))
            {
                return true;
            }

            await Task.Delay(250, ct);
        }

        return false;
    }
}
