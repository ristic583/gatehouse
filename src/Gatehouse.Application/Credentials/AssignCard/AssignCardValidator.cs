using FluentValidation;
using Gatehouse.Application.Common.Validation;

namespace Gatehouse.Application.Credentials.AssignCard;

/// <summary>Request shape of <c>POST …/cards</c>, mirroring <c>Card</c>.</summary>
public sealed partial class AssignCardValidator : AbstractValidator<AssignCardRequest>
{
    public AssignCardValidator() =>
        RuleFor(r => r.CardNumber).Must(n => n is not null && CardNumber().IsMatch(n.Trim()))
            .WithProblem("card.invalid_number", "Card number must be 6-20 digits.");

    [System.Text.RegularExpressions.GeneratedRegex("^[0-9]{6,20}$")]
    private static partial System.Text.RegularExpressions.Regex CardNumber();
}
