using FluentValidation;
using Gatehouse.Application.Common.Validation;
using Gatehouse.Application.Sites.Dtos;

namespace Gatehouse.Application.Sites.Admin;

/// <summary>Create and update reader: name shape, mirroring <c>Reader</c>; the position needs the floor (handler).</summary>
public sealed class SaveReaderValidator : AbstractValidator<SaveReaderRequest>
{
    public SaveReaderValidator() =>
        RuleFor(r => r.Name).Must(SiteRules.IsName).WithProblem("reader.invalid_name", "Reader name must be 1-100 characters.");
}
