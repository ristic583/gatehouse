using System.Net.Http.Json;
using CloudPacs.Simulator;
using Gatehouse.Adapters.CloudPacs;
using Gatehouse.Connector.Contracts.Adapters;
using Gatehouse.Connector.Contracts.Events;
using Gatehouse.ContractTests.Conformance;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gatehouse.ContractTests.CloudPacs;

public sealed class CloudPacsFixture : IPacsAdapterFixture
{
    private static readonly Dictionary<DoorEventType, string> VendorTypes = new()
    {
        [DoorEventType.AccessGranted] = "access.granted",
        [DoorEventType.AccessDenied] = "access.denied",
        [DoorEventType.DoorOpened] = "door.opened",
        [DoorEventType.DoorClosed] = "door.closed",
        [DoorEventType.DoorHeldOpen] = "door.held_open",
        [DoorEventType.DoorForced] = "door.forced",
        [DoorEventType.DoorUnlocked] = "door.unlocked",
        [DoorEventType.DoorRelocked] = "door.relocked",
    };

    private readonly HttpClient _adapterHttp = new();
    private WebApplication _app = null!;
    private HttpClient _dev = null!;
    private CloudPacsAdapter _adapter = null!;

    public IPacsAdapter Adapter => _adapter;

    public string KnownAccessLevelRef => "AG-AAR-DOCK";

    public string KnownPacsDoorRef => "D-AAR-DOCK";

    public async ValueTask InitializeAsync()
    {
        _app = CloudPacsSimulatorProgram.CreateApp(["--urls=http://127.0.0.1:0"]);
        await _app.StartAsync();
        var baseUrl = new Uri(_app.Urls.First() + "/");
        _dev = new HttpClient { BaseAddress = baseUrl };
        _adapter = new CloudPacsAdapter(
            new CloudPacsAdapterOptions(baseUrl, CloudPacsOptions.DefaultApiKey, "http://127.0.0.1:0"),
            _adapterHttp, NullLogger<CloudPacsAdapter>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await _adapter.DisposeAsync();
        _adapterHttp.Dispose();
        _dev.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    public async Task InjectTransientFailureAsync(int count)
    {
        using var response = await _dev.PostAsJsonAsync("dev/chaos", new { failNext = count });
        response.EnsureSuccessStatusCode();
    }

    public async Task EmitDoorEventAsync(string pacsDoorRef, DoorEventType type, string? cardNumber)
    {
        using var response = await _dev.PostAsJsonAsync($"dev/doors/{Uri.EscapeDataString(pacsDoorRef)}/events",
            new { type = VendorTypes[type], cardNumber });
        response.EnsureSuccessStatusCode();
    }
}
