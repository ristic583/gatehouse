using Gatehouse.Connector.Contracts.Adapters;
using Gatehouse.Connector.Contracts.Events;

namespace Gatehouse.ContractTests.Conformance;

public interface IPacsAdapterFixture : IAsyncLifetime
{
    public IPacsAdapter Adapter { get; }

    public string KnownAccessLevelRef { get; }

    public string KnownPacsDoorRef { get; }

    public Task InjectTransientFailureAsync(int count);

    public Task EmitDoorEventAsync(string pacsDoorRef, DoorEventType type, string? cardNumber);
}
