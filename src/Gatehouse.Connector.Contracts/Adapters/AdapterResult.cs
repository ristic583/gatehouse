using Gatehouse.Connector.Contracts.Events;

namespace Gatehouse.Connector.Contracts.Adapters;

public sealed record AdapterResult(AdapterStatus Status, string? PacsRef, AdapterError? Error)
{
    public static AdapterResult Applied(string? pacsRef) => new(AdapterStatus.Applied, pacsRef, null);
    public static AdapterResult Failed(string code, string message, bool retryable) =>
        new(AdapterStatus.Failed, null, new(code, message, retryable));
    public static AdapterResult Unsupported(string capability) =>
        new(AdapterStatus.Unsupported, null, new("adapter.capability_missing", $"{capability} not supported", false));
}
