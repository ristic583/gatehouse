using FluentValidation;
using Gatehouse.Application.Platform.Dtos;

namespace Gatehouse.Application.Platform.SuspendTenant;

public sealed class SuspendTenantValidator : AbstractValidator<SuspendTenantRequest>
{
    public SuspendTenantValidator() => RuleFor(r => r.Reason).NotEmpty().MaximumLength(500);
}
