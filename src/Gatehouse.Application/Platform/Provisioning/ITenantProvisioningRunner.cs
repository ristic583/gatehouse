namespace Gatehouse.Application.Platform.Provisioning;

/// <summary>Turns <c>Provisioning</c> catalog rows into active tenants, one idempotent step at a time (ADR-009 §3).</summary>
public interface ITenantProvisioningRunner
{
    /// <summary>Runs every provisioning row in state <c>Running</c> until it completes or fails. Returns the number of rows
    /// processed.</summary>
    public Task<int> RunOnceAsync(CancellationToken ct);
}
