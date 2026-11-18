using System.Net.Mail;
using Gatehouse.Application.Common.Messaging;
using Microsoft.Extensions.Options;

namespace Gatehouse.Infrastructure.Messaging;

/// <summary>
/// Plain SMTP relay without authentication (a relay on the local network); disabled when <c>Notifications:SmtpHost</c> is empty.
/// </summary>
public sealed class SmtpEmailSender(IOptions<NotificationOptions> options) : IEmailSender
{
    private readonly NotificationOptions _options = options.Value;

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_options.SmtpHost) && !string.IsNullOrWhiteSpace(_options.From);

    public async Task SendAsync(string recipient, string subject, string body, CancellationToken ct)
    {
        if (!IsEnabled)
        {
            return;
        }

        using var client = new SmtpClient(_options.SmtpHost, _options.SmtpPort) { DeliveryMethod = SmtpDeliveryMethod.Network };
        using var message = new MailMessage(_options.From!, recipient, subject, body);
        await client.SendMailAsync(message, ct);
    }
}
