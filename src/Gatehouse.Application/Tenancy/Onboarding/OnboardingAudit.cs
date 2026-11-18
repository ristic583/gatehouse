using Gatehouse.Application.Audit;
using Gatehouse.Application.Identity;
using Gatehouse.Domain.Audit;

namespace Gatehouse.Application.Tenancy.Onboarding;

/// <summary>Records an onboarding change on the tenant with the signed-in person as actor (caller saves).</summary>
public sealed class OnboardingAudit(ITenantContext tenant, ICurrentUser user, IAuditLog audit)
{
    public async Task RecordAsync(string action, object? payload, CancellationToken ct) =>
        audit.Record(AuditActor.ForPerson(await user.RequirePersonIdAsync(ct)), action, "Tenant", tenant.TenantId.Value, payload);
}
