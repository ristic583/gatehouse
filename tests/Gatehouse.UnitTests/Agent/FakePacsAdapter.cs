using Gatehouse.Connector.Contracts.Adapters;

namespace Gatehouse.UnitTests.Agent;

internal sealed class FakePacsAdapter : IPacsAdapter
{
    public int GrantCalls { get; private set; }

    public AdapterResult GrantResult { get; set; } = AdapterResult.Applied("ref-1");

    public bool ThrowOnGrant { get; set; }

    public PacsCapabilities Capabilities { get; set; } = new("Fake", "1", true, false, false, false, true, [], 10);

    public List<AccessRevoke> Revokes { get; } = [];

    public AdapterResult RevokeResult { get; set; } = AdapterResult.Applied(null);

    public Task<AdapterResult> GrantAccessAsync(AccessGrant grant, CancellationToken ct)
    {
        GrantCalls++;
        return ThrowOnGrant ? throw new InvalidOperationException("boom") : Task.FromResult(GrantResult);
    }

    public Task<AdapterResult> UpsertCardholderAsync(Cardholder cardholder, CancellationToken ct) => Task.FromResult(AdapterResult.Applied(null));

    public Task<AdapterResult> RevokeAccessAsync(AccessRevoke revoke, CancellationToken ct)
    {
        Revokes.Add(revoke);
        return Task.FromResult(RevokeResult);
    }

    public Task<AdapterResult> SetCardStateAsync(CardStateChange change, CancellationToken ct) => Task.FromResult(AdapterResult.Applied(null));

    public Task<AdapterResult> SetPinAsync(PinWrite pin, CancellationToken ct) => Task.FromResult(AdapterResult.Unsupported("pinWrite"));

    public TimeSpan DoorDelay { get; set; }

    public int DoorCalls { get; private set; }

    public async Task<AdapterResult> DoorCommandAsync(DoorCommand command, CancellationToken ct)
    {
        DoorCalls++;
        if (DoorDelay > TimeSpan.Zero)
        {
            await Task.Delay(DoorDelay, ct);
        }

        return AdapterResult.Applied(null);
    }

    public async IAsyncEnumerable<PacsDoorEvent> StreamEventsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask;
        yield break;
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
}
