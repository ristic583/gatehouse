using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;

namespace Gatehouse.Domain.Access;

public static class AccessRequestPolicy
{
    public static Result<AccessRequestDecision> Evaluate(AccessRequestInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var eligibility = input.Eligibility;

        if (input.SubjectStatus != PersonStatus.Active)
        {
            return Error.Validation("request.person_inactive", "The person is not active.");
        }

        var justification = input.Justification?.Trim() ?? string.Empty;
        if (justification.Length is < 10 or > 500)
        {
            return Error.Validation("request.justification_invalid", "Justification must be 10-500 characters.");
        }

        if (input.RequestedStart >= input.RequestedEnd)
        {
            return Error.Validation("request.invalid_window", "Start must be before end.");
        }

        if (input.RequestedStart < input.Now.AddMinutes(-5))
        {
            return Error.Validation("request.start_in_past", "Start must not be in the past.");
        }

        if (input.RequestedStart < eligibility.Start)
        {
            return Error.Validation("request.before_eligibility", "Start is before the person's eligibility.");
        }

        if (eligibility.End is { } eligibilityEnd && input.RequestedStart >= eligibilityEnd)
        {
            return Error.Validation("request.outside_eligibility", "Start is after the person's eligibility ends.");
        }

        if ((input.RequestedEnd - input.RequestedStart).TotalDays > input.MaxDurationDays)
        {
            return Error.Validation(
                "request.exceeds_max_duration",
                $"The requested window exceeds the maximum of {input.MaxDurationDays} days for this area.");
        }

        if (!input.AreaMapped)
        {
            return Error.Validation("request.area_not_mapped", "This area is not mapped to any access control system.");
        }

        var duplicate = input.ExistingForArea.Any(e =>
            e.Status == EntitlementStatus.PendingApproval
            || (e.Status == EntitlementStatus.Approved && e.EffectiveEnd > input.Now));
        if (duplicate)
        {
            return Error.Conflict("request.duplicate", "An open request or active access already exists for this area.");
        }

        var effectiveEnd = eligibility.End is { } end && end < input.RequestedEnd ? end : input.RequestedEnd;
        return new AccessRequestDecision(effectiveEnd, effectiveEnd < input.RequestedEnd);
    }
}
