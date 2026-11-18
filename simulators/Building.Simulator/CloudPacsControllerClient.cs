using System.Net;
using System.Net.Http.Json;

namespace Building.Simulator;

/// <summary>Typed client for the CloudPACS simulator's <c>/dev/*</c> endpoints (base URL <c>Simulator:CloudPacsUrl</c>).</summary>
public sealed class CloudPacsControllerClient(HttpClient http) : IDoorControllerClient
{
    public async Task<ControllerDecision?> SwipeAsync(SwipeCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var response = await http.PostAsJsonAsync("/dev/swipes", new { doorRef = command.DoorRef, cardNumber = command.CardNumber, externalRef = command.PersonRef }, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<SwipeResponse>(ct)
            ?? throw new InvalidOperationException("CloudPACS returned an empty swipe response.");
        return new ControllerDecision(body.Decision == "granted" ? "Granted" : "Denied", body.EventId);
    }

    public async Task DoorEventAsync(string doorRef, string type, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync($"/dev/doors/{Uri.EscapeDataString(doorRef)}/events", new { type = ToVendorType(type) }, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<ControllerDoor>> ListDoorsAsync(CancellationToken ct) =>
        [.. (await http.GetFromJsonAsync<List<DoorResponse>>("/dev/doors", ct) ?? []).Select(d => new ControllerDoor(d.DoorRef, d.GroupId, false))];

    public async Task<IReadOnlyList<ControllerAssignment>> ListAssignmentsAsync(CancellationToken ct) =>
        [.. (await http.GetFromJsonAsync<List<AssignmentResponse>>("/dev/assignments", ct) ?? [])
            .Select(a => new ControllerAssignment(a.ExternalRef, a.CardNumber, a.GroupId, a.Active))];

    /// <summary>Maps a LegacyPACS door event type name to the CloudPACS vendor type.</summary>
    public static string ToVendorType(string type) => type switch
    {
        "DoorOpened" => "door.opened",
        "DoorClosed" => "door.closed",
        "DoorHeldOpen" => "door.held_open",
        "DoorForced" => "door.forced",
        "DoorUnlocked" => "door.unlocked",
        "DoorRelocked" => "door.relocked",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown door event type."),
    };

    private sealed record SwipeResponse(string Decision, long EventId);

    private sealed record DoorResponse(string DoorRef, string GroupId);

    private sealed record AssignmentResponse(string ExternalRef, string? CardNumber, string GroupId, bool Active);
}
