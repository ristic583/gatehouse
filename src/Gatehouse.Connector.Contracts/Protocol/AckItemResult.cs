namespace Gatehouse.Connector.Contracts.Protocol;

public sealed record AckItemResult(Guid CommandId, bool Accepted, string? Code);
