using Gatehouse.Connector.Contracts.Events;

namespace Gatehouse.Connector.Contracts.Adapters;

public sealed record AdapterError(string Code, string Message, bool Retryable);
