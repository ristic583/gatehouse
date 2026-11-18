namespace Gatehouse.Connector.Contracts.Protocol;

public sealed record AckResponse(IReadOnlyList<AckItemResult> Results);
