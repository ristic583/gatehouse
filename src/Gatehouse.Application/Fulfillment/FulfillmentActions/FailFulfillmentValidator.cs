using FluentValidation;
using Gatehouse.Application.Common.Validation;
using Gatehouse.Application.Fulfillment.Dtos;

namespace Gatehouse.Application.Fulfillment.FulfillmentActions;

/// <summary>Request shape of <c>POST …/fail</c>.</summary>
public sealed class FailFulfillmentValidator : AbstractValidator<FailFulfillmentDto>
{
    public FailFulfillmentValidator() =>
        RuleFor(r => r.Reason).Must(r => r?.Trim() is { Length: >= 1 and <= 500 })
            .WithProblem("fulfillment.reason_required", "A reason of 1-500 characters is required.");
}
