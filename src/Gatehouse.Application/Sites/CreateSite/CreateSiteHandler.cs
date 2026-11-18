using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Sites.Admin;
using Gatehouse.Application.Sites.Dtos;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Sites;

namespace Gatehouse.Application.Sites.CreateSite;

/// <summary><c>POST /api/v1/admin/sites</c> (<c>domain-model.md</c> §2.1); audit <c>site.created</c>.</summary>
public sealed class CreateSiteHandler(ITenantDb db, AdminAudit audit)
{
    public async Task<Result<SiteDto>> HandleAsync(CreateSiteCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var request = command.Request;
        ArgumentNullException.ThrowIfNull(request);
        var site = Site.Create(
            request.Name?.Trim() ?? string.Empty, request.Code?.Trim() ?? string.Empty, request.TimeZoneId?.Trim() ?? string.Empty);
        if (site.IsFailure)
        {
            return site.Error;
        }

        var unique = await SiteUniqueness.CheckAsync(db, site.Value.Name, site.Value.Code, null, ct);
        if (unique.IsFailure)
        {
            return unique.Error;
        }

        db.Sites.Add(site.Value);
        await audit.RecordAsync("site.created", "Site", site.Value.Id.Value, new { name = site.Value.Name, code = site.Value.Code }, ct);
        await db.SaveChangesAsync(ct);
        return SiteAdminMapper.ToDto(site.Value, []);
    }
}
