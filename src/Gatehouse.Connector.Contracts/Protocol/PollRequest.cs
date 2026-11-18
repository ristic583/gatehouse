namespace Gatehouse.Connector.Contracts.Protocol;

public sealed record PollRequest(int MaxBatch = 50, int WaitSeconds = 25);
