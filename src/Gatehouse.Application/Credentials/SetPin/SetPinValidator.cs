using FluentValidation;
using Gatehouse.Application.Common.Validation;
using Gatehouse.Domain.Credentials;

namespace Gatehouse.Application.Credentials.SetPin;

/// <summary>Request shape of <c>PUT /api/v1/me/pin</c>, mirroring <c>Pin</c>.</summary>
public sealed class SetPinValidator : AbstractValidator<SetPinRequest>
{
    public SetPinValidator() =>
        RuleFor(r => r.Pin).Must(p => Pin.Create(p).IsSuccess).WithProblem("pin.invalid", "A PIN must be 4-8 digits.");
}
