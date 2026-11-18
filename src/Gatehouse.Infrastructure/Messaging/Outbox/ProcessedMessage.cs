namespace Gatehouse.Infrastructure.Messaging.Outbox;

public sealed class ProcessedMessage
{
    public Guid MessageId { get; set; }

    public string Consumer { get; set; } = string.Empty;

    public DateTimeOffset ProcessedAt { get; set; }
}
