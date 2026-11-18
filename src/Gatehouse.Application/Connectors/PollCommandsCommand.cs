namespace Gatehouse.Application.Connectors;

/// <summary>
/// <c>POST /connector/v1/poll</c>: up to <paramref name="MaxBatch"/> commands, waiting at most <paramref name="WaitSeconds"/>.
/// </summary>
public sealed record PollCommandsCommand(ConnectorContext Connector, int MaxBatch, int WaitSeconds);
