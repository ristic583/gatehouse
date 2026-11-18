using FluentValidation;
using Gatehouse.Application.Common.Validation;
using Gatehouse.Application.Sites.Dtos;

namespace Gatehouse.Application.Sites.Admin;

/// <summary>Create and update site: request shape, mirroring <c>Site</c> (which keeps the same checks as invariants).</summary>
public sealed class SaveSiteValidator : AbstractValidator<SaveSiteRequest>
{
    public SaveSiteValidator()
    {
        RuleFor(r => r.Name).Must(SiteRules.IsName).WithProblem("site.invalid_name", "Site name must be 1-100 characters.");
        RuleFor(r => r.Code).Must(c => c is not null && SiteRules.SiteCode().IsMatch(c.Trim()))
            .WithProblem("site.invalid_code", "Site code must match ^[A-Z0-9-]{2,20}$.");
        RuleFor(r => r.TimeZoneId).Must(z => !string.IsNullOrWhiteSpace(z) && TimeZoneInfo.TryFindSystemTimeZoneById(z.Trim(), out _))
            .WithProblem("site.invalid_time_zone", "Time zone must be a valid IANA id.");
    }
}
