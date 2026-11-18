using Gatehouse.Domain.Common;

namespace Gatehouse.Domain.People;

public sealed record Eligibility
{
    private Eligibility(DateTimeOffset start, DateTimeOffset? end, string timeZoneId)
    {
        Start = start;
        End = end;
        TimeZoneId = timeZoneId;
    }

    public DateTimeOffset Start { get; private init; }

    public DateTimeOffset? End { get; private init; }

    public string TimeZoneId { get; private init; }

    public static Result<Eligibility> Create(DateTimeOffset start, DateTimeOffset? end, string timeZoneId)
    {
        if (end is not null && end <= start)
        {
            return Error.Validation("person.invalid_eligibility", "Eligibility end must be after its start.");
        }

        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return Error.Validation("person.invalid_eligibility", "Eligibility needs a time zone.");
        }

        return new Eligibility(start, end, timeZoneId);
    }

    public DateRange AsRange() => new(Start, End);
}
