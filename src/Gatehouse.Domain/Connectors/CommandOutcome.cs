namespace Gatehouse.Domain.Connectors;

public enum CommandOutcome
{
    None,
    Requeued,
    Acked,
    RetryScheduled,
    Failed,
    Duplicate,
    Conflict,
}
