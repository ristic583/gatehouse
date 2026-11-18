using Gatehouse.Application.Identity;
using Gatehouse.Application.LiveBuilding;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Sites;

namespace Gatehouse.Api.Features.LiveBuilding;

/// <summary>Hub invocations and background revalidation have no HTTP tenant middleware: resolve tenant + current user in a fresh
/// scope.</summary>
public sealed class LiveSiteAccessChecker(ITenantScopeFactory scopes) : ILiveSiteAccessChecker
{
    public async Task<bool> CanViewAsync(string tenant, string subject, string? email, Guid siteId, CancellationToken ct)
    {
        var tenantId = TenantId.Create(tenant);
        if (tenantId.IsFailure)
        {
            return false;
        }

        await using var scope = await scopes.CreateScopeAsync(tenantId.Value, ct);
        await scope.ServiceProvider.GetRequiredService<CurrentUserAccessor>().LoadAsync(subject, email, ct);
        return await scope.ServiceProvider.GetRequiredService<LiveSiteAccess>().CanViewAsync(new SiteId(siteId), ct);
    }
}
