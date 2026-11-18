using Gatehouse.Application.Authorization.Resources;
using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.Application.Reconciliation;

/// <summary>Authorization resource of a PACS instance for the reconciliation requirements (authorization.md §10).</summary>
public static class ReconciliationScope
{
    public static PacsInstanceResource ResourceFor(PacsInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return new PacsInstanceResource(instance.Id, instance.SiteIds.ToHashSet());
    }
}
