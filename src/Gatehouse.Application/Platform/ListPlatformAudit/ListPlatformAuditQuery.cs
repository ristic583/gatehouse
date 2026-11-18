namespace Gatehouse.Application.Platform.ListPlatformAudit;

/// <summary><paramref name="Before"/> is a sequence number (exclusive); newest first, at most 200.</summary>
public sealed record ListPlatformAuditQuery(string? TenantId, long? Before, int? Limit);
