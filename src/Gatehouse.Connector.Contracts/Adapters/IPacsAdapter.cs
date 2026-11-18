using Gatehouse.Connector.Contracts.Events;

namespace Gatehouse.Connector.Contracts.Adapters;

public interface IPacsAdapter
{
    public PacsCapabilities Capabilities { get; }

    public Task<AdapterResult> UpsertCardholderAsync(Cardholder cardholder, CancellationToken ct);
    public Task<AdapterResult> GrantAccessAsync(AccessGrant grant, CancellationToken ct);
    public Task<AdapterResult> RevokeAccessAsync(AccessRevoke revoke, CancellationToken ct);
    public Task<AdapterResult> SetCardStateAsync(CardStateChange change, CancellationToken ct);
    public Task<AdapterResult> SetPinAsync(PinWrite pin, CancellationToken ct);
    public Task<AdapterResult> DoorCommandAsync(DoorCommand command, CancellationToken ct);

    public IAsyncEnumerable<PacsDoorEvent> StreamEventsAsync(CancellationToken ct);
    public IAsyncEnumerable<PacsRecord> ExportRecordsAsync(ExportScope scope, CancellationToken ct);
}
