namespace Gatehouse.Application.Common.Messaging;

/// <summary>Config section <c>Notifications</c>: SMTP relay for approver emails (lab finding #10). Empty <see cref="SmtpHost"/> =
/// off.</summary>
public sealed class NotificationOptions
{
    public const string Section = "Notifications";

    public string? SmtpHost { get; set; }

    public int SmtpPort { get; set; } = 25;

    public string? From { get; set; }

    /// <summary>Public web URL used for links in emails (e.g. <c>https://gatehouse.example.com</c>).</summary>
    public string? PublicUrl { get; set; }
}
