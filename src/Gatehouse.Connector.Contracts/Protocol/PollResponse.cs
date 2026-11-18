namespace Gatehouse.Connector.Contracts.Protocol;

public sealed record PollResponse(DateTimeOffset ServerTime, IReadOnlyList<CommandEnvelope> Commands);
