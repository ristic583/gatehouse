using FluentValidation;
using Gatehouse.Application.Common.Validation;
using Gatehouse.Application.Sites.Dtos;

namespace Gatehouse.Application.Sites.Admin;

/// <summary>Create and update door: name shape, mirroring <c>Door</c>; position and target area need the floor (handler).</summary>
public sealed class SaveDoorValidator : AbstractValidator<SaveDoorRequest>
{
    public SaveDoorValidator() =>
        RuleFor(r => r.Name).Must(SiteRules.IsName).WithProblem("door.invalid_name", "Door name must be 1-100 characters.");
}
