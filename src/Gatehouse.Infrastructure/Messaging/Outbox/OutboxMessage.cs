using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.Infrastructure.Messaging.Outbox;

public sealed class OutboxMessage
{
    public Guid Id { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public string Type { get; set; } = string.Empty;

    public string Payload { get; set; } = "{}";

    public CommandPriority Priority { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset NextAttemptAt { get; set; }

    public DateTimeOffset? DispatchedAt { get; set; }

    public string? LastError { get; set; }

    /// <summary>W3C traceparent of the activity that wrote the row (M9); the relay restores it as the publish span's parent.</summary>
    public string? TraceParent { get; set; }
}
