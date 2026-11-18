namespace Gatehouse.Agent.Transport;

/// <summary>The gateway refused a request for good (404/409/413) with a problem <see cref="Code"/>.</summary>
public sealed class GatewayRejectedException(int statusCode, string? code)
    : Exception($"Gateway rejected the request with HTTP {statusCode} ({code})")
{
    public int StatusCode { get; } = statusCode;

    public string? Code { get; } = code;
}
