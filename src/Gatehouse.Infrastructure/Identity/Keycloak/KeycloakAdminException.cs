namespace Gatehouse.Infrastructure.Identity.Keycloak;

/// <summary>A refused admin call. Carries the operation and status only, never request or response bodies (they may hold
/// secrets).</summary>
public sealed class KeycloakAdminException : Exception
{
    public KeycloakAdminException(string operation, int statusCode)
        : base($"Keycloak admin call '{operation}' failed with HTTP {statusCode}.")
    {
        Operation = operation;
        StatusCode = statusCode;
    }

    public KeycloakAdminException()
        : this("unknown", 0)
    {
    }

    public KeycloakAdminException(string message)
        : base(message)
    {
        Operation = "unknown";
    }

    public KeycloakAdminException(string message, Exception innerException)
        : base(message, innerException)
    {
        Operation = "unknown";
    }

    public string Operation { get; } = "unknown";

    public int StatusCode { get; }
}
