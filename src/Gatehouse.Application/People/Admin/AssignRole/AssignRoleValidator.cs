using FluentValidation;
using Gatehouse.Application.Common.Validation;
using Gatehouse.Application.People.Dtos;
using Gatehouse.Domain.People;

namespace Gatehouse.Application.People.Admin.AssignRole;

/// <summary>Request shape of <c>POST /api/v1/admin/role-assignments</c>, mirroring <c>RoleAssignment</c>.</summary>
public sealed class AssignRoleValidator : AbstractValidator<AssignRoleRequest>
{
    public AssignRoleValidator() =>
        RuleFor(r => r).Must(r => r.Role != AssignedRole.TenantAdmin || r.SiteId is null)
            .WithProblem("role.tenant_admin_cannot_be_site_scoped", "Tenant admin cannot be scoped to a site.");
}
