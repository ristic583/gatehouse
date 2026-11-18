using System.Text.RegularExpressions;
using FluentValidation;
using Gatehouse.Application.Platform.Dtos;
using Gatehouse.Domain.Common;

namespace Gatehouse.Application.Platform.CreateTenant;

public sealed partial class CreateTenantValidator : AbstractValidator<CreateTenantRequest>
{
    public CreateTenantValidator()
    {
        RuleFor(r => r.TenantId).Must(id => TenantId.Create(id).IsSuccess).WithErrorCode("tenant.invalid_id")
            .WithMessage("Tenant id must be 3-32 characters: a lowercase letter followed by lowercase letters, digits or dashes.");
        RuleFor(r => r.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(r => r.EmailDomain).NotEmpty().MaximumLength(253).Matches(DomainPattern())
            .WithMessage("Email domain must be a lowercase DNS name with a dot, such as example.com.");
        RuleFor(r => r.AdminEmail).NotEmpty().MaximumLength(254).EmailAddress();
        // Stricter than the contract: the first admin is invited only inside the customer's own domain.
        RuleFor(r => r.AdminEmail)
            .Must((r, email) => email is not null && r.EmailDomain is not null
                && email.Trim().EndsWith("@" + r.EmailDomain, StringComparison.OrdinalIgnoreCase))
            .WithMessage("The admin email must be in the tenant's email domain.");
        RuleFor(r => r.AdminGivenName).NotEmpty().MaximumLength(100);
        RuleFor(r => r.AdminFamilyName).NotEmpty().MaximumLength(100);
        RuleFor(r => r.TimeZoneId).NotEmpty().Must(z => TimeZoneInfo.TryFindSystemTimeZoneById(z ?? string.Empty, out _))
            .WithMessage("Unknown IANA time zone.");
    }

    [GeneratedRegex("^(?=.{4,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\\.)+[a-z][a-z0-9-]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex DomainPattern();
}
