using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Identity;

/// <summary>
/// Scoped <see cref="ICurrentUser"/>. <see cref="LoadAsync"/> runs once per request (after tenant resolution):
/// finds the person by identity subject, else links it by email, then loads role assignments.
/// </summary>
public sealed class CurrentUserAccessor(ITenantContext tenantContext, IServiceProvider services) : ICurrentUser
{
    private bool _loaded;

    public TenantId TenantId => tenantContext.TenantId;

    public string Subject { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public PersonId? PersonId { get; private set; }

    public PersonType? PersonType { get; private set; }

    public string? PersonDisplayName { get; private set; }

    public bool IsTenantAdmin { get; private set; }

    public bool IsSecurityStaff { get; private set; }

    public IReadOnlySet<SiteId>? SecurityStaffSiteIds { get; private set; }

    public async Task LoadAsync(string subject, string? email, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        Subject = subject;
        Email = (email ?? string.Empty).Trim().ToLowerInvariant();

        if (!tenantContext.IsResolved || subject.Length == 0)
        {
            return;
        }

        var db = (ITenantDb)services.GetService(typeof(ITenantDb))!;
        var person = await db.Persons.SingleOrDefaultAsync(p => p.IdentitySubject == subject, ct);
        if (person is null && Email.Length > 0)
        {
            person = await db.Persons.SingleOrDefaultAsync(p => p.Email == Email && p.IdentitySubject == null, ct);
            // A parallel first request may have committed the link between the two lookups above: read it by subject again.
            person ??= await db.Persons.AsNoTracking().SingleOrDefaultAsync(p => p.IdentitySubject == subject, ct);
            if (person is not null && person.IdentitySubject is null && person.LinkIdentity(subject).IsSuccess)
            {
                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException)
                {
                    // First sign-in race: a parallel request linked the same person first (row version / unique subject). Use its link.
                    db.Persons.Entry(person).State = EntityState.Detached;
                    person = await db.Persons.AsNoTracking().SingleOrDefaultAsync(p => p.IdentitySubject == subject, ct);
                }
            }
        }

        if (person is null || person.Status != PersonStatus.Active)
        {
            return;
        }

        PersonId = person.Id;
        PersonType = person.Type;
        PersonDisplayName = person.DisplayName;

        var personId = person.Id;
        var assignments = await db.RoleAssignments.AsNoTracking().Where(r => r.PersonId == personId).ToListAsync(ct);
        IsTenantAdmin = assignments.Any(r => r.Role == AssignedRole.TenantAdmin);
        var staff = assignments.Where(r => r.Role == AssignedRole.SecurityStaff).ToList();
        IsSecurityStaff = staff.Count > 0;
        SecurityStaffSiteIds = !IsSecurityStaff || staff.Any(r => r.SiteId is null)
            ? null
            : staff.Select(r => r.SiteId!.Value).ToHashSet();
    }

    public Task<PersonId> RequirePersonIdAsync(CancellationToken ct) =>
        PersonId is { } id ? Task.FromResult(id) : throw new PersonNotProvisionedException();
}
