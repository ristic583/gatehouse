using Gatehouse.Domain.Common;
using Gatehouse.Domain.Platform;

namespace Gatehouse.Application.Platform;

public interface IPlatformAuditLog
{
    /// <summary>Appends a chained entry to the catalog context; the caller saves it.</summary>
    public Task WriteAsync(PlatformActor actor, string action, TenantId? tenantId, object? data, CancellationToken ct);
}
