using FluentValidation;
using Gatehouse.Application.Common.Validation;

namespace Gatehouse.Application.Fulfillment.PacsAdmin;

/// <summary>Create and update PACS instance: name shape, mirroring <c>PacsInstance</c>; sites need the database (handler).</summary>
public sealed class SavePacsInstanceValidator : AbstractValidator<SavePacsInstanceRequest>
{
    public SavePacsInstanceValidator() =>
        RuleFor(r => r.Name).Must(n => !string.IsNullOrWhiteSpace(n) && n.Trim().Length <= 100)
            .WithProblem("pacs.invalid_name", "PACS instance name must be 1-100 characters.");
}
