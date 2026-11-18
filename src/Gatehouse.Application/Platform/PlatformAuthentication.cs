namespace Gatehouse.Application.Platform;

/// <summary>Platform operator authentication (ADR-009 §1): its own scheme, never the tenant default scheme.</summary>
public static class PlatformAuthentication
{
    public const string Scheme = "PlatformBearer";
    public const string RoutePrefix = "/api/v1/platform";
}
