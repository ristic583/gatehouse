namespace Gatehouse.Connector.Contracts.Protocol;

public sealed record HeartbeatResponse(DateTimeOffset ServerTime, int PollWaitSecondsHint, string? DesiredLogLevel);
