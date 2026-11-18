using System.Security.Claims;

namespace Gatehouse.Application.Platform;

public interface IPlatformOperatorResolver
{
    /// <summary>Links the principal to an active operator (by <c>sub</c>, else by email with no subject yet).</summary>
    public Task<bool> ResolveAsync(ClaimsPrincipal principal, CancellationToken ct);
}
