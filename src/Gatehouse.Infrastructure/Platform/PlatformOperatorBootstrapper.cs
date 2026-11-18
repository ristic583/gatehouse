using Gatehouse.Domain.Platform;
using Gatehouse.Infrastructure.Persistence.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Infrastructure.Platform;

/// <summary>CLI <c>bootstrap</c>: ensures the operator from <c>Platform:BootstrapOperatorEmail</c> exists (idempotent).</summary>
public sealed class PlatformOperatorBootstrapper(CatalogDbContext catalog, TimeProvider time)
{
    public const string EmailSetting = "Platform:BootstrapOperatorEmail";

    /// <returns>True when the operator was created.</returns>
    public async Task<bool> EnsureAsync(string email, CancellationToken ct)
    {
        var normalized = email.Trim().ToLowerInvariant();
        if (await catalog.PlatformOperators.AnyAsync(o => o.Email == normalized, ct))
        {
            return false;
        }

        var created = PlatformOperator.Create(normalized, "Platform operator", time.GetUtcNow());
        if (created.IsFailure)
        {
            throw new InvalidOperationException($"{EmailSetting} is not a valid email address.");
        }

        catalog.PlatformOperators.Add(created.Value);
        await catalog.SaveChangesAsync(ct);
        return true;
    }
}
