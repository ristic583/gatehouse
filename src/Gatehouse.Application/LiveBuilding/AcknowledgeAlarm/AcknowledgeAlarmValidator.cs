using FluentValidation;
using Gatehouse.Application.Common.Validation;
using Gatehouse.Domain.LiveBuilding;

namespace Gatehouse.Application.LiveBuilding.AcknowledgeAlarm;

/// <summary>Request shape of <c>POST …/acknowledge</c>, mirroring <c>Alarm</c>.</summary>
public sealed class AcknowledgeAlarmValidator : AbstractValidator<AcknowledgeAlarmRequest>
{
    public AcknowledgeAlarmValidator() =>
        RuleFor(r => r.Comment).Must(c => c?.Trim() is { Length: >= 1 and <= Alarm.MaxCommentLength })
            .WithProblem("alarm.comment_required", "A comment of 1-500 characters is required to acknowledge an alarm.");
}
