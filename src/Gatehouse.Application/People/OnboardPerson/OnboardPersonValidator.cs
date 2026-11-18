using FluentValidation;
using Gatehouse.Domain.People;

namespace Gatehouse.Application.People.OnboardPerson;

/// <summary>Request shape only; caps, sponsor and email rules live in the handler and <see cref="Person"/>.</summary>
public sealed class OnboardPersonValidator : AbstractValidator<OnboardPersonCommand>
{
    private static readonly string[] Types = [nameof(PersonType.External), nameof(PersonType.Visitor), nameof(PersonType.Guest)];

    public OnboardPersonValidator()
    {
        RuleFor(c => c.Request).NotNull();
        When(c => c.Request is not null, () =>
        {
            RuleFor(c => c.Request.Type).Must(t => Types.Contains(t)).WithMessage("Type must be External, Visitor or Guest.");
            RuleFor(c => c.Request.GivenName).NotEmpty().MaximumLength(100);
            RuleFor(c => c.Request.FamilyName).NotEmpty().MaximumLength(100);
            RuleFor(c => c.Request.Email).NotEmpty().EmailAddress().MaximumLength(254);
            RuleFor(c => c.Request.Start).NotEmpty();
            RuleFor(c => c.Request.End).NotEmpty().WithMessage("An end date is required.");
            RuleFor(c => c.Request.End).GreaterThan(c => c.Request.Start).When(c => c.Request.End != default);
            RuleFor(c => c.Request.TimeZoneId).NotEmpty();
            RuleFor(c => c.Request.CardNumber).Matches("^[0-9]{6,20}$").When(c => !string.IsNullOrEmpty(c.Request.CardNumber));
        });
    }
}
