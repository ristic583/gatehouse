using System.Security.Cryptography;
using System.Text;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.Domain.Connectors;

public sealed class ConnectorRegistration : Entity<ConnectorId>
{
    public static readonly TimeSpan EnrollmentTokenLifetime = TimeSpan.FromHours(24);

    private ConnectorRegistration(
        ConnectorId id,
        TenantId tenantId,
        PacsInstanceId pacsInstanceId,
        string displayName,
        string tokenHash,
        DateTimeOffset now)
        : base(id)
    {
        TenantId = tenantId;
        PacsInstanceId = pacsInstanceId;
        DisplayName = displayName;
        Status = ConnectorStatus.PendingEnrollment;
        EnrollmentTokenHash = tokenHash;
        EnrollmentTokenExpiresAt = now + EnrollmentTokenLifetime;
        CreatedAt = now;
    }

    // EF Core
    private ConnectorRegistration()
        : base(default)
    {
        DisplayName = string.Empty;
    }

    public TenantId TenantId { get; private set; }

    public PacsInstanceId PacsInstanceId { get; private set; }

    public string DisplayName { get; private set; }

    public ConnectorStatus Status { get; private set; }

    public string? EnrollmentTokenHash { get; private set; }

    public DateTimeOffset? EnrollmentTokenExpiresAt { get; private set; }

    public string? CertificateThumbprint { get; private set; }

    public DateTimeOffset? CertificateNotAfter { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? LastSeenAt { get; private set; }

    public static ConnectorRegistration Create(
        TenantId tenantId,
        PacsInstanceId pacsInstanceId,
        string displayName,
        string tokenHash,
        DateTimeOffset now) =>
        new(ConnectorId.New(), tenantId, pacsInstanceId, displayName, tokenHash, now);

    public Result Enroll(string tokenHash, string thumbprint, DateTimeOffset notAfter, DateTimeOffset now)
    {
        if (Status != ConnectorStatus.PendingEnrollment
            || EnrollmentTokenHash is null
            || !FixedEquals(EnrollmentTokenHash, tokenHash))
        {
            return Error.Unauthorized("connector.enrollment_invalid", "Enrollment token is invalid.");
        }

        if (EnrollmentTokenExpiresAt is { } exp && now > exp)
        {
            return Error.Unauthorized("connector.enrollment_expired", "Enrollment token has expired.");
        }

        Status = ConnectorStatus.Active;
        EnrollmentTokenHash = null;
        EnrollmentTokenExpiresAt = null;
        CertificateThumbprint = thumbprint;
        CertificateNotAfter = notAfter;
        LastSeenAt = now;
        return Result.Success();
    }

    public void Revoke(DateTimeOffset now)
    {
        Status = ConnectorStatus.Revoked;
        EnrollmentTokenHash = null;
        EnrollmentTokenExpiresAt = null;
        LastSeenAt ??= now;
    }

    public void Touch(DateTimeOffset now) => LastSeenAt = now;

    private static bool FixedEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
