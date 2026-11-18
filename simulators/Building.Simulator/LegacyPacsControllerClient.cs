using System.Net;
using System.Net.Http.Json;

namespace Building.Simulator;

/// <summary>Typed client for the LegacyPACS simulator's controller endpoints (base URL <c>Simulator:LegacyPacsUrl</c>).</summary>
public sealed class LegacyPacsControllerClient(HttpClient http) : IDoorControllerClient
{
    public async Task<ControllerDecision?> SwipeAsync(SwipeCommand command, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("/controller/swipe", command, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ControllerDecision>(ct);
    }

    public async Task DoorEventAsync(string doorRef, string type, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("/controller/door-events", new { doorRef, type }, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<ControllerDoor>> ListDoorsAsync(CancellationToken ct) =>
        await http.GetFromJsonAsync<List<ControllerDoor>>("/doors", ct) ?? [];

    public async Task<IReadOnlyList<ControllerAssignment>> ListAssignmentsAsync(CancellationToken ct) =>
        await http.GetFromJsonAsync<List<ControllerAssignment>>("/assignments", ct) ?? [];

    public async Task SetSlowAsync(int delayMs, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("/chaos", new { failNext = 0, failNextPermanent = 0, delayMs }, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task ClearChaosAsync(CancellationToken ct)
    {
        using var response = await http.DeleteAsync("/chaos", ct);
        response.EnsureSuccessStatusCode();
    }
}
