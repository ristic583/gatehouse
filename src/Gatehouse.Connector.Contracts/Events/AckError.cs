namespace Gatehouse.Connector.Contracts.Events;

public sealed record AckError(string Code, string Message, bool Retryable);
