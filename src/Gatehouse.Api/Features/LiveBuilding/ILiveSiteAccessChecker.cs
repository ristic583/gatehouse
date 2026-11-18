namespace Gatehouse.Api.Features.LiveBuilding;

/// <summary>Whether a user (by token subject/email) may currently view a site's live feed (<c>ViewLiveSiteRequirement</c>).</summary>
public interface ILiveSiteAccessChecker
{
    public Task<bool> CanViewAsync(string tenant, string subject, string? email, Guid siteId, CancellationToken ct);
}
