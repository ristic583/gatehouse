using FluentValidation;
using Gatehouse.Application.Common.Validation;
using Gatehouse.Application.People.Dtos;

namespace Gatehouse.Application.People.Admin.CreateEmployee;

/// <summary>Request shape of <c>POST /api/v1/admin/people/employees</c>, mirroring <c>Person</c>.</summary>
public sealed class CreateEmployeeValidator : AbstractValidator<CreateEmployeeRequest>
{
    public CreateEmployeeValidator()
    {
        RuleFor(r => r).Must(r => IsName(r.GivenName) && IsName(r.FamilyName))
            .WithProblem("person.invalid_name", "Given and family name must be 1-100 characters.");
        RuleFor(r => r.Email).Must(e => e?.Trim() is { Length: >= 1 and <= 254 } t && t.Contains('@', StringComparison.Ordinal))
            .WithProblem("person.invalid_email", "A valid email address is required.");
    }

    private static bool IsName(string? name) => !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 100;
}
