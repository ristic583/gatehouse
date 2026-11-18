using Gatehouse.Connector.Contracts.Events;

namespace Gatehouse.Connector.Contracts.Adapters;

public interface IAdapterCursorStore
{
    public ValueTask<string?> GetAsync(string name, CancellationToken ct);
    public ValueTask SetAsync(string name, string value, CancellationToken ct);
}
