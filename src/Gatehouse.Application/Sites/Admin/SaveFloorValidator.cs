using FluentValidation;
using Gatehouse.Application.Common.Validation;
using Gatehouse.Application.Sites.Dtos;

namespace Gatehouse.Application.Sites.Admin;

/// <summary>Create and update floor: request shape, mirroring <c>Floor</c>.</summary>
public sealed class SaveFloorValidator : AbstractValidator<SaveFloorRequest>
{
    public SaveFloorValidator()
    {
        RuleFor(r => r.Name).Must(SiteRules.IsName).WithProblem("floor.invalid_name", "Floor name must be 1-100 characters.");
        RuleFor(r => r.Level).InclusiveBetween(-5, 200).WithProblem("floor.invalid_level", "Floor level must be between -5 and 200.");
        RuleFor(r => r).Must(r => r.PlanWidth is >= 100 and <= 10000 && r.PlanHeight is >= 100 and <= 10000)
            .WithProblem("floor.invalid_plan_size", "Plan width and height must be 100-10000.");
    }
}
