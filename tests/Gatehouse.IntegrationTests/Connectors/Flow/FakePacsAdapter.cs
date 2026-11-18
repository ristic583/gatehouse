using Gatehouse.Connector.Contracts.Adapters;

namespace Gatehouse.IntegrationTests.Connectors.Flow;

/// <summary>Records adapter calls in order; <see cref="GrantResult"/> is scriptable.</summary>
internal sealed class FakePacsAdapter : IPacsAdapter
{
    public List<string> Calls { get; } = [];

    public AdapterResult GrantResult { get; set; } = AdapterResult.Applied("ref-grant");

    /// <summary>Events yielded (once each) by <see cref="StreamEventsAsync"/>.</summary>
    public Queue<PacsDoorEvent> Events { get; } = new();

    public PacsCapabilities Capabilities { get; } = new("Fake", "1", true, false, false, false, true, [], 10);

    public Task<AdapterResult> GrantAccessAsync(AccessGrant grant, CancellationToken ct) => Record("GrantAccess", GrantResult);

    public Task<AdapterResult> UpsertCardholderAsync(Cardholder cardholder, CancellationToken ct) => Record("UpsertCardholder", AdapterResult.Applied("ref-upsert"));

    public Task<AdapterResult> RevokeAccessAsync(AccessRevoke revoke, CancellationToken ct) => Record("RevokeAccess", AdapterResult.Applied("ref-revoke"));

    public Task<AdapterResult> SetCardStateAsync(CardStateChange change, CancellationToken ct) => Record("SetCardState", AdapterResult.Applied(null));

    public Task<AdapterResult> SetPinAsync(PinWrite pin, CancellationToken ct) => Record("SetPin", AdapterResult.Unsupported("pinWrite"));

    public Task<AdapterResult> DoorCommandAsync(DoorCommand command, CancellationToken ct) => Record("DoorCommand", AdapterResult.Applied(null));

    public async IAsyncEnumerable<PacsDoorEvent> StreamEventsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask;
        while (Events.TryPeek(out var next))
        {
            yield return next;
            Events.Dequeue(); // cursor advances only after the consumer stored the item
        }
    }

    public List<PacsRecord> ExportedRecords { get; } = [];

    public async IAsyncEnumerable<PacsRecord> ExportRecordsAsync(ExportScope scope, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask;
        foreach (var record in ExportedRecords)
        {
            yield return record;
        }
    }

    private Task<AdapterResult> Record(string call, AdapterResult result)
    {
        Calls.Add(call);
        return Task.FromResult(result);
    }
}
