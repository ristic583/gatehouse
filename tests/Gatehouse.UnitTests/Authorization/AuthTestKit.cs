using System.Security.Claims;
using Gatehouse.Application.Authorization.Resources;
using Gatehouse.Application.Identity;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;
using Microsoft.AspNetCore.Authorization;

namespace Gatehouse.UnitTests.Authorization;

internal sealed class FakeCurrentUser(PersonId? personId, bool admin = false, bool staff = false, IReadOnlySet<SiteId>? staffSites = null) : ICurrentUser
{
    public TenantId TenantId => new("nordlys");

    public string Subject => "sub";

    public string Email => "me@x.test";

    public PersonId? PersonId => personId;

    public bool IsTenantAdmin => admin;

    public bool IsSecurityStaff => staff;

    public IReadOnlySet<SiteId>? SecurityStaffSiteIds => staffSites;

    public Task<PersonId> RequirePersonIdAsync(CancellationToken ct) => Task.FromResult(personId!.Value);
}

internal static class AuthTestKit
{
    public static readonly SiteId Site = SiteId.New();

    public static AuthorizationHandlerContext Run(IAuthorizationHandler handler, IAuthorizationRequirement requirement, object? resource)
    {
        var context = new AuthorizationHandlerContext([requirement], new ClaimsPrincipal(), resource);
        handler.HandleAsync(context).GetAwaiter().GetResult();
        return context;
    }

    public static PersonResource Person(PersonId id, PersonId? manager = null, PersonId? sponsor = null) =>
        new(id, PersonType.Employee, manager, sponsor);

    public static EntitlementResource Entitlement(PersonResource subject, PersonId requester, params PersonId[] owners) =>
        new(EntitlementId.New(), subject, requester, new AreaResource(AreaId.New(), Site, owners.ToHashSet()));

    public static ApprovalStepResource Step(EntitlementResource entitlement, ApproverKind kind, PersonId? assigned = null) =>
        new(entitlement, ApprovalStepId.New(), kind, assigned);
}
