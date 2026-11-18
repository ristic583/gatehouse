using FluentValidation;
using Gatehouse.Application.Common.Validation;
using Gatehouse.Application.Sites.Dtos;

namespace Gatehouse.Application.Sites.Admin;

/// <summary>Create and update area: name and code shape, mirroring <c>Area</c>; the polygon needs the floor plan (handler).</summary>
public sealed class SaveAreaValidator : AbstractValidator<SaveAreaRequest>
{
    public SaveAreaValidator()
    {
        RuleFor(r => r.Name).Must(SiteRules.IsName).WithProblem("area.invalid_name", "Area name must be 1-100 characters.");
        RuleFor(r => r.Code).Must(c => c is not null && SiteRules.AreaCode().IsMatch(c.Trim()))
            .WithProblem("area.invalid_code", "Area code must match ^[A-Z0-9-]{2,30}$.");
    }
}
