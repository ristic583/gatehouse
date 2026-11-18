namespace Gatehouse.Domain.Common;

public readonly record struct DateRange(DateTimeOffset Start, DateTimeOffset? End)
{
    public bool Contains(DateTimeOffset instant) => Start <= instant && (End is null || instant < End);
}
