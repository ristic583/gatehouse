using FluentValidation;
using Gatehouse.Domain.LiveBuilding;

namespace Gatehouse.Application.LiveBuilding.RequestDoorCommand;

/// <summary>Request shape only; <c>DoorCommand.Request</c> re-checks reason and duration.</summary>
public sealed class RequestDoorCommandValidator : AbstractValidator<RequestDoorCommandCommand>
{
    public RequestDoorCommandValidator()
    {
        RuleFor(c => c.Kind).IsInEnum();
        RuleFor(c => c.Scope).IsInEnum();
        RuleFor(c => c.ScopeId).NotEmpty();
        RuleFor(c => c.Reason).NotEmpty().MaximumLength(DoorCommand.MaxReasonLength);
        When(c => c.Kind == DoorCommandKind.MomentaryUnlock, () =>
        {
            RuleFor(c => c.Scope).Equal(DoorCommandScope.Door).WithMessage("Momentary unlock targets a single door.");
            RuleFor(c => c.DurationSeconds).NotNull().InclusiveBetween(1, DoorCommand.MaxUnlockSeconds);
        }).Otherwise(() => RuleFor(c => c.DurationSeconds).Null().WithMessage("Lockdown takes no duration."));
    }
}
