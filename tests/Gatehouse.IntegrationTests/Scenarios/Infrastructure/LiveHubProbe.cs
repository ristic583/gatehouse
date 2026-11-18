using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace Gatehouse.IntegrationTests.Scenarios.Infrastructure;

/// <summary>
/// A live-building SignalR connection like the web's (<c>/api/proxy/v1/live/hub</c> through the BFF, session cookie, SSE transport) that
/// records when each <c>live</c> update arrives.
/// </summary>
public sealed class LiveHubProbe : IAsyncDisposable
{
    private readonly HubConnection _connection;
    private readonly ConcurrentQueue<DateTimeOffset> _received = new();

    private LiveHubProbe(HubConnection connection)
    {
        _connection = connection;
        _connection.On<JsonElement>("live", _ => _received.Enqueue(DateTimeOffset.UtcNow));
    }

    public IReadOnlyList<DateTimeOffset> Received => [.. _received];

    public static async Task<LiveHubProbe> JoinSiteAsync(ScenarioClient client, Guid siteId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(client);
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(client.Web, "/api/proxy/v1/live/hub"), HttpTransportType.ServerSentEvents, options =>
            {
                options.HttpMessageHandlerFactory = _ => client.CreateCookieHandler();
                options.Headers["Origin"] = client.Web.GetLeftPart(UriPartial.Authority);
            })
            .Build();
        var probe = new LiveHubProbe(connection);
        try
        {
            await connection.StartAsync(ct);
            await connection.InvokeAsync("JoinSite", siteId, ct);
            return probe;
        }
        catch
        {
            await probe.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
}
