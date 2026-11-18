using System.Security.Claims;
using Gatehouse.Application.Platform;
using Gatehouse.Domain.Platform;
using Gatehouse.Infrastructure.Persistence.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Infrastructure.Platform;

/// <summary>
/// Scoped: links the <c>PlatformBearer</c> principal to a catalog <see cref="PlatformOperator"/> (authorization.md §11).
/// </summary>
public sealed class PlatformOperatorAccessor(CatalogDbContext catalog) : IPlatformOperator, IPlatformOperatorResolver
{
    private PlatformOperator? _operator;

    public bool IsResolved => _operator is not null;

    public PlatformOperatorId Id => Current.Id;

    public string Subject => Current.Subject ?? string.Empty;

    public string Email => Current.Email;

    public string DisplayName => Current.DisplayName;

    private PlatformOperator Current =>
        _operator ?? throw new InvalidOperationException("No platform operator is resolved for this request.");

    public async Task<bool> ResolveAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (_operator is not null)
        {
            return true;
        }

        var subject = principal.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(subject))
        {
            return false;
        }

        var found = await catalog.PlatformOperators.SingleOrDefaultAsync(o => o.Subject == subject, ct);
        if (found is null)
        {
            var email = principal.FindFirst("email")?.Value?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(email))
            {
                return false;
            }

            found = await catalog.PlatformOperators.SingleOrDefaultAsync(o => o.Email == email && o.Subject == null, ct);
            if (found is null || found.Status != PlatformOperatorStatus.Active)
            {
                return false;
            }

            found.LinkSubject(subject);
            await catalog.SaveChangesAsync(ct);
        }

        if (found.Status != PlatformOperatorStatus.Active)
        {
            return false;
        }

        _operator = found;
        return true;
    }
}
