using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Platform.Provisioning;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Infrastructure.Tenancy;

/// <summary>Tenant-side provisioning steps <c>Baseline</c> and <c>FirstAdmin</c> (<c>domain-model.md</c> §2.10). Both are
/// idempotent.</summary>
public sealed class TenantBaselineWriter(ITenantDb db, IAuditLog audit, TimeProvider time)
{
    /// <summary>The four default security levels of §2.1.</summary>
    public static readonly IReadOnlyList<(string Name, int Rank, ApproverChain Chain, int MaxDays)> DefaultSecurityLevels =
    [
        ("Low", 1, ApproverChain.Manager, 365),
        ("Medium", 2, ApproverChain.AreaOwner, 365),
        ("High", 3, ApproverChain.Both, 182),
        ("Restricted", 4, ApproverChain.Both, 30),
    ];

    public async Task<Result> WriteBaselineAsync(TenantId tenantId, CancellationToken ct)
    {
        if (!await db.SecurityLevels.AnyAsync(ct))
        {
            foreach (var (name, rank, chain, maxDays) in DefaultSecurityLevels)
            {
                var level = SecurityLevel.Create(name, rank, chain, maxDays);
                if (level.IsFailure)
                {
                    return level.Error;
                }

                db.SecurityLevels.Add(level.Value);
            }
        }

        if (!await db.AuditEntries.AnyAsync(e => e.Action == ProvisioningActions.TenantChainProvisioned, ct))
        {
            audit.Record(AuditActor.ForSystem("platform"), ProvisioningActions.TenantChainProvisioned, "Tenant", tenantId.Value);
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> EnsureFirstAdminAsync(
        string email, string givenName, string familyName, string timeZoneId, CancellationToken ct)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var person = await db.Persons.SingleOrDefaultAsync(p => p.Email == normalized, ct);
        if (person is null)
        {
            var now = time.GetUtcNow();
            var eligibility = Eligibility.Create(now, null, timeZoneId);
            if (eligibility.IsFailure)
            {
                return eligibility.Error;
            }

            var created = Person.Create(
                PersonSource.Authoritative, PersonType.Employee, givenName, familyName, normalized, eligibility.Value, null, null, now);
            if (created.IsFailure)
            {
                return created.Error;
            }

            person = created.Value;
            db.Persons.Add(person);
        }

        var personId = person.Id;
        if (!await db.RoleAssignments.AnyAsync(r => r.PersonId == personId && r.Role == AssignedRole.TenantAdmin, ct))
        {
            var role = RoleAssignment.Create(personId, AssignedRole.TenantAdmin, null);
            if (role.IsFailure)
            {
                return role.Error;
            }

            db.RoleAssignments.Add(role.Value);
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
