namespace Gatehouse.Agent.Transport;

public sealed class GatewayTransientException(string message, TimeSpan? retryAfter, Exception? inner = null) : Exception(message, inner)
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
