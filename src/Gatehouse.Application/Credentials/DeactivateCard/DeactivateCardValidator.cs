using FluentValidation;
using Gatehouse.Application.Common.Validation;
using Gatehouse.Domain.Credentials;

namespace Gatehouse.Application.Credentials.DeactivateCard;

/// <summary>Request shape of <c>POST …/deactivate</c>, mirroring <c>Card</c>.</summary>
public sealed class DeactivateCardValidator : AbstractValidator<DeactivateCardRequest>
{
    public DeactivateCardValidator() =>
        RuleFor(r => r.Reason).Must(r => !string.IsNullOrWhiteSpace(r) && r.Trim().Length <= Card.ReasonMaxLength)
            .WithProblem("card.reason_required", $"A reason of 1-{Card.ReasonMaxLength} characters is required.");
}
