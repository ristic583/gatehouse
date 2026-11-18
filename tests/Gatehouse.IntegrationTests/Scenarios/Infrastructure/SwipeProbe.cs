using System.Net.Http.Json;
using Building.Simulator;

namespace Gatehouse.IntegrationTests.Scenarios.Infrastructure;

/// <summary>Swipes a card at a door through the Building.Simulator, which asks the PACS that owns the door.</summary>
public sealed class SwipeProbe(HttpClient buildingSim)
{
    public async Task<ControllerDecision> SwipeAsync(string doorRef, string cardNumber, CancellationToken ct, string direction = "In")
    {
        using var response = await buildingSim.PostAsJsonAsync(
            new Uri("/swipes", UriKind.Relative), new SwipeCommand(doorRef, CardNumber: cardNumber, Direction: direction), ScenarioJson.Options, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Swipe at {doorRef}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(ct)}");
        }

        return (await response.Content.ReadFromJsonAsync<ControllerDecision>(ScenarioJson.Options, ct))!;
    }

    /// <summary>Starts a Building.Simulator scenario (<c>pacs-slow</c>, …); disposing the handle clears it.</summary>
    public async Task<IAsyncDisposable> ScenarioAsync(string name, CancellationToken ct)
    {
        using (var response = await buildingSim.PostAsJsonAsync(new Uri($"/scenarios/{name}", UriKind.Relative), new { doorRef = (string?)null }, ct))
        {
            response.EnsureSuccessStatusCode();
        }

        return new ClearScenario(buildingSim, name);
    }

    private sealed class ClearScenario(HttpClient sim, string name) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            using var response = await sim.PostAsync(new Uri($"/scenarios/{name}/clear", UriKind.Relative), null, CancellationToken.None);
            response.EnsureSuccessStatusCode();
        }
    }
}
