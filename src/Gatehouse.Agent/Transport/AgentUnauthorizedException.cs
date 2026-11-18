namespace Gatehouse.Agent.Transport;

public sealed class AgentUnauthorizedException(int statusCode) : Exception($"Gateway rejected the agent with HTTP {statusCode}")
{
    public int StatusCode { get; } = statusCode;
}
