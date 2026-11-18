using Gatehouse.Domain.Common;

namespace Gatehouse.Domain.Platform;

public sealed class PlatformOperator : AggregateRoot<PlatformOperatorId>
{
    private PlatformOperator(PlatformOperatorId id, string email, string displayName, DateTimeOffset createdAt)
        : base(id)
    {
        Email = email;
        DisplayName = displayName;
        CreatedAt = createdAt;
        Status = PlatformOperatorStatus.Active;
    }

    // EF Core
    private PlatformOperator()
        : base(default)
    {
        Email = string.Empty;
        DisplayName = string.Empty;
    }

    public string Email { get; private set; }

    public string DisplayName { get; private set; }

    public string? Subject { get; private set; }

    public PlatformOperatorStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Result<PlatformOperator> Create(string email, string displayName, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@', StringComparison.Ordinal))
        {
            return Error.Validation("validation.failed", "Operator email is invalid.");
        }

        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 100)
        {
            return Error.Validation("validation.failed", "Operator display name must be 1-100 characters.");
        }

        return new PlatformOperator(PlatformOperatorId.New(), email.Trim().ToLowerInvariant(), displayName, now);
    }

    public void LinkSubject(string subject) => Subject = subject;

    public void Disable() => Status = PlatformOperatorStatus.Disabled;
}
