namespace Gatehouse.Application.Common.Messaging;

/// <summary>Outgoing plain-text email (SMTP relay); <see cref="IsEnabled"/> is false when no relay is configured.</summary>
public interface IEmailSender
{
    public bool IsEnabled { get; }

    public Task SendAsync(string recipient, string subject, string body, CancellationToken ct);
}
