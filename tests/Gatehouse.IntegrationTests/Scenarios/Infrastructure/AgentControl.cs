using Aspire.Hosting;
using Gatehouse.Application.LiveBuilding.Dtos;

namespace Gatehouse.IntegrationTests.Scenarios.Infrastructure;

/// <summary>Stops and starts connector agents through Aspire resource commands and waits for the connector health to follow.</summary>
public sealed class AgentControl(ScenarioAppHostFixture fixture)
{
    public const string HqAgent = "agent-nordlys-hq";
    public const string AarhusAgent = "agent-nordlys-aarhus";
    private static readonly TimeSpan StateTimeout = TimeSpan.FromMinutes(3);

    public async Task StopAsync(string resourceName, CancellationToken ct)
    {
        await ExecuteAsync(resourceName, "resource-stop", ct);
        await WaitForOnlineAsync(resourceName, online: false, ct);
    }

    public async Task StartAsync(string resourceName, CancellationToken ct)
    {
        await ExecuteAsync(resourceName, "resource-start", ct);
        await WaitForOnlineAsync(resourceName, online: true, ct);
    }

    public static string PacsInstanceOf(string resourceName) => resourceName switch
    {
        HqAgent => "HQ-Copenhagen",
        AarhusAgent => "Warehouse-Aarhus",
        _ => throw new ArgumentOutOfRangeException(nameof(resourceName), resourceName, "Unknown agent resource."),
    };

    private async Task ExecuteAsync(string resourceName, string command, CancellationToken ct)
    {
        var result = await fixture.App.ResourceCommands.ExecuteCommandAsync(resourceName, command, ct);
        if (!result.Success)
        {
            throw new InvalidOperationException($"{command} on {resourceName} failed: {result.Message}");
        }
    }

    /// <summary>Waits until <c>GET /api/v1/live/connectors</c> (security staff) shows the agent's PACS instance online/offline.</summary>
    private async Task WaitForOnlineAsync(string resourceName, bool online, CancellationToken ct)
    {
        var name = PacsInstanceOf(resourceName);
        var staff = await fixture.ClientAsync(ScenarioPersonas.SecurityStaff, ct);
        var reached = await PacsProbe.WaitUntilAsync(
            async c => (await staff.GetAsync<List<ConnectorHealthDto>>("v1/live/connectors", c)).Any(h => h.Name == name && h.Online == online),
            StateTimeout,
            ct);
        if (!reached)
        {
            throw new TimeoutException($"{name} did not become {(online ? "online" : "offline")} within {StateTimeout}.");
        }
    }
}
