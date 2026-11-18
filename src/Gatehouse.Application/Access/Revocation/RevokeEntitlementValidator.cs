using FluentValidation;
using Gatehouse.Application.Access.Dtos;
using Gatehouse.Application.Common.Validation;

namespace Gatehouse.Application.Access.Revocation;

/// <summary>Request shape of revoke and emergency revoke, mirroring <c>Entitlement.Revoke</c>.</summary>
public sealed class RevokeEntitlementValidator : AbstractValidator<RevokeEntitlementDto>
{
    public RevokeEntitlementValidator() =>
        RuleFor(r => r.Reason).Must(r => !string.IsNullOrWhiteSpace(r) && r.Length <= 500)
            .WithProblem("entitlement.revocation_reason_required", "Revoking requires a reason (1-500 characters).");
}
