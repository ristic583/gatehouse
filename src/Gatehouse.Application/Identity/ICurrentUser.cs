using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;

namespace Gatehouse.Application.Identity;

public interface ICurrentUser
{
    public TenantId TenantId { get; }

    public string Subject { get; }

    public string Email { get; }

    public PersonId? PersonId { get; }

    public bool IsTenantAdmin { get; }

    public bool IsSecurityStaff { get; }

    public IReadOnlySet<SiteId>? SecurityStaffSiteIds { get; }

    public Task<PersonId> RequirePersonIdAsync(CancellationToken ct);
}
