using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tenancy;

namespace Gatehouse.Application.Platform;

/// <summary>Used when no distributed channel is configured (single process, CLI, tests): the handler's local eviction is all there
/// is.</summary>
public sealed class LocalTenantStatusNotifier : ITenantStatusNotifier
{
    public Task PublishAsync(TenantId tenantId, TenantStatus status, CancellationToken ct) => Task.CompletedTask;
}
