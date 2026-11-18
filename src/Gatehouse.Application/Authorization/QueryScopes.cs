using Gatehouse.Application.Identity;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;
using Gatehouse.Domain.Tasks;

namespace Gatehouse.Application.Authorization;

/// <summary>Row-level scope for list queries (authorization.md §5). All predicates translate to SQL.</summary>
public static class QueryScopes
{
    public static IQueryable<Person> VisibleTo(this IQueryable<Person> persons, ICurrentUser me)
    {
        ArgumentNullException.ThrowIfNull(me);
        if (me.PersonId is not { } id)
        {
            return persons.Where(_ => false);
        }

        if (me.IsTenantAdmin || (me.IsSecurityStaff && me.SecurityStaffSiteIds is null))
        {
            return persons;
        }

        PersonId? nullableId = id;
        return persons.Where(p => p.Id == id || p.ManagerId == nullableId || p.SponsorId == nullableId);
    }

    public static IQueryable<Entitlement> VisibleTo(
        this IQueryable<Entitlement> entitlements,
        ICurrentUser me,
        IQueryable<Area> areas,
        IQueryable<Person> persons,
        IQueryable<Floor> floors)
    {
        ArgumentNullException.ThrowIfNull(me);
        if (me.PersonId is not { } id)
        {
            return entitlements.Where(_ => false);
        }

        if (me.IsTenantAdmin || (me.IsSecurityStaff && me.SecurityStaffSiteIds is null))
        {
            return entitlements;
        }

        PersonId? nullableId = id;
        var staffSites = me.IsSecurityStaff && me.SecurityStaffSiteIds is { } ss ? ss.ToList() : [];
        var siteScoped = staffSites.Count > 0;
        return entitlements.Where(e =>
            (siteScoped && areas.Any(a => a.Id == e.AreaId && floors.Any(f => f.Id == a.FloorId && staffSites.Contains(f.SiteId))))
            || e.PersonId == id
            || e.RequestedById == id
            || persons.Any(p => p.Id == e.PersonId && (p.ManagerId == nullableId || p.SponsorId == nullableId))
            || areas.Any(a => a.Id == e.AreaId && a.Owners.Any(o => o.PersonId == id)));
    }

    public static IQueryable<Entitlement> AwaitingDecisionBy(this IQueryable<Entitlement> entitlements, PersonId me, IQueryable<Area> areas)
    {
        PersonId? nullableMe = me;
        return entitlements.Where(e =>
            e.Status == EntitlementStatus.PendingApproval
            && e.PersonId != me
            && e.RequestedById != me
            && e.Steps.Any(s => s.Status == ApprovalStepStatus.Pending
                && ((s.ApproverKind == ApproverKind.Manager && s.AssignedApproverId == nullableMe)
                    || (s.ApproverKind == ApproverKind.AreaOwner
                        && areas.Any(a => a.Id == e.AreaId && a.Owners.Any(o => o.PersonId == me))))));
    }

    public static IQueryable<Domain.Fulfillment.Fulfillment> HandledBy(
        this IQueryable<Domain.Fulfillment.Fulfillment> fulfillments,
        ICurrentUser me,
        IQueryable<Entitlement> entitlements,
        IQueryable<Area> areas,
        IQueryable<Floor> floors)
    {
        ArgumentNullException.ThrowIfNull(me);
        if (!me.IsSecurityStaff)
        {
            return fulfillments.Where(_ => false);
        }

        if (me.SecurityStaffSiteIds is not { } sites)
        {
            return fulfillments;
        }

        var staffSites = sites.ToList();
        return fulfillments.Where(f => entitlements.Any(e => e.Id == f.EntitlementId
            && areas.Any(a => a.Id == e.AreaId && floors.Any(fl => fl.Id == a.FloorId && staffSites.Contains(fl.SiteId)))));
    }

    public static IQueryable<StaffTask> VisibleTo(this IQueryable<StaffTask> tasks, ICurrentUser me)
    {
        ArgumentNullException.ThrowIfNull(me);
        if (me.IsTenantAdmin || (me.IsSecurityStaff && me.SecurityStaffSiteIds is null))
        {
            return tasks;
        }

        if (!me.IsSecurityStaff)
        {
            return tasks.Where(_ => false);
        }

        var sites = me.SecurityStaffSiteIds!.Select(s => (SiteId?)s).ToList();
        return tasks.Where(t => sites.Contains(t.SiteId));
    }
}
