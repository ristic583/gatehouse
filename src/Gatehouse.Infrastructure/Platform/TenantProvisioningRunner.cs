using Gatehouse.Application.Platform;
using Gatehouse.Application.Platform.Provisioning;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Platform;
using Gatehouse.Infrastructure.Persistence;
using Gatehouse.Infrastructure.Persistence.Catalog;
using Gatehouse.Infrastructure.Security;
using Gatehouse.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Gatehouse.Infrastructure.Platform;

/// <summary>
/// ADR-009 §3 step machine. Each step is idempotent; a failure stores the step and a secret-free error so a retry resumes there.
/// The tenant is not <c>Active</c> yet, so <c>Baseline</c>/<c>FirstAdmin</c> bind a scope to the tenant directly instead of
/// going through <see cref="ITenantScopeFactory"/> (which refuses inactive tenants).
/// </summary>
public sealed partial class TenantProvisioningRunner(
    IServiceScopeFactory scopes,
    MigrationRunner migrations,
    ISecretProtector protector,
    TimeProvider time,
    ILogger<TenantProvisioningRunner> logger) : ITenantProvisioningRunner
{
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        List<TenantId> running;
        await using (var scope = scopes.CreateAsyncScope())
        {
            running = await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().TenantProvisionings.AsNoTracking()
                .Where(p => p.State == ProvisioningState.Running)
                .OrderBy(p => p.StartedAt)
                .Select(p => p.TenantId)
                .ToListAsync(ct);
        }

        foreach (var tenantId in running)
        {
            await RunTenantAsync(tenantId, ct);
        }

        return running.Count;
    }

    private async Task RunTenantAsync(TenantId tenantId, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await using var scope = scopes.CreateAsyncScope();
            var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var provisioning = await catalog.TenantProvisionings.SingleOrDefaultAsync(p => p.TenantId == tenantId, ct);
            var tenant = await catalog.Tenants.SingleOrDefaultAsync(t => t.Id == tenantId, ct);
            if (provisioning is null || tenant is null || provisioning.State != ProvisioningState.Running)
            {
                return;
            }

            var step = provisioning.Step;
            Result outcome;
            try
            {
                outcome = await RunStepAsync(scope.ServiceProvider, step, tenant, provisioning, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
#pragma warning disable CA1031 // host boundary: a failing step is recorded, never rethrown into the job loop
            catch (Exception ex)
#pragma warning restore CA1031
            {
                // Only the exception type: messages of database/HTTP exceptions may carry hosts or connection details.
                LogStepException(ex, tenantId.Value, step);
                outcome = Error.Failure("tenant.provisioning_failed", $"Step {step} failed ({ex.GetType().Name}).");
            }

            if (outcome.IsFailure)
            {
                await RecordFailureAsync(tenantId, step, outcome.Error, ct);
                return;
            }

            var now = time.GetUtcNow();
            var advanced = provisioning.Advance(now);
            if (advanced.IsFailure)
            {
                return;
            }

            var platformAudit = scope.ServiceProvider.GetRequiredService<IPlatformAuditLog>();
            if (step == ProvisioningStep.AdminInvite)
            {
                await platformAudit.WriteAsync(PlatformActor.System, ProvisioningActions.AdminInviteSent, tenantId, null, ct);
            }

            if (provisioning.Step == ProvisioningStep.Completed)
            {
                var provisioned = tenant.MarkProvisioned();
                if (provisioned.IsFailure)
                {
                    await RecordFailureAsync(tenantId, provisioning.Step, provisioned.Error, ct);
                    return;
                }

                await platformAudit.WriteAsync(PlatformActor.System, ProvisioningActions.TenantProvisioned, tenantId, null, ct);
                await catalog.SaveChangesAsync(ct);
                LogProvisioned(tenantId.Value);
                return;
            }

            await catalog.SaveChangesAsync(ct);
        }
    }

    private async Task<Result> RunStepAsync(
        IServiceProvider services, ProvisioningStep step, Domain.Tenancy.Tenant tenant, TenantProvisioning provisioning,
        CancellationToken ct)
    {
        var tenantId = tenant.Id;
        switch (step)
        {
            case ProvisioningStep.Database:
                var encrypted = await services.GetRequiredService<TenantProvisioningService>().EnsureDatabaseAsync(tenantId, ct);
                tenant.SetConnection(encrypted);
                return Result.Success();

            case ProvisioningStep.Migrate:
                var migrated = await migrations.MigrateTenantAsync(tenantId, ct);
                return migrated.Succeeded
                    ? Result.Success()
                    : Error.Failure("tenant.migration_failed", "Tenant database migration failed.");

            case ProvisioningStep.Baseline:
                return await WithTenantAsync(tenant, w => w.WriteBaselineAsync(tenantId, ct));

            case ProvisioningStep.FirstAdmin:
                return await WithTenantAsync(tenant, w => w.EnsureFirstAdminAsync(
                    provisioning.AdminEmail, provisioning.AdminGivenName, provisioning.AdminFamilyName, provisioning.TimeZoneId, ct));

            case ProvisioningStep.Organization:
                return await services.GetRequiredService<IIdentityProvisioner>()
                    .EnsureOrganizationAsync(tenantId, tenant.DisplayName, tenant.EmailDomain ?? string.Empty, ct);

            case ProvisioningStep.AdminInvite:
                return await services.GetRequiredService<IIdentityProvisioner>().InviteTenantAdminAsync(
                    tenantId, provisioning.AdminEmail, provisioning.AdminGivenName, provisioning.AdminFamilyName, ct);

            default:
                return Result.Success();
        }
    }

    private async Task<Result> WithTenantAsync(Domain.Tenancy.Tenant tenant, Func<TenantBaselineWriter, Task<Result>> action)
    {
        if (string.IsNullOrEmpty(tenant.EncryptedConnectionString))
        {
            return Error.Failure("tenant.provisioning_failed", "The tenant database is not set up.");
        }

        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenant.Id, protector.Unprotect(tenant.EncryptedConnectionString));
        return await action(scope.ServiceProvider.GetRequiredService<TenantBaselineWriter>());
    }

    private async Task RecordFailureAsync(TenantId tenantId, ProvisioningStep step, Error error, CancellationToken ct)
    {
        // Fresh scope: the failed step may have left tracked changes behind.
        await using var scope = scopes.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var provisioning = await catalog.TenantProvisionings.SingleAsync(p => p.TenantId == tenantId, ct);
        var tenant = await catalog.Tenants.SingleAsync(t => t.Id == tenantId, ct);
        var now = time.GetUtcNow();
        provisioning.Fail($"{error.Code}: {error.Message}", now);
        _ = tenant.MarkProvisioningFailed();
        await scope.ServiceProvider.GetRequiredService<IPlatformAuditLog>().WriteAsync(
            PlatformActor.System, ProvisioningActions.TenantProvisioningFailed, tenantId, new { step = step.ToString(), code = error.Code },
            ct);
        await catalog.SaveChangesAsync(ct);
        LogFailed(tenantId.Value, step, error.Code);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Tenant {TenantId} provisioned")]
    private partial void LogProvisioned(string tenantId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Provisioning of tenant {TenantId} failed at step {Step} with {Code}")]
    private partial void LogFailed(string tenantId, ProvisioningStep step, string code);

    [LoggerMessage(Level = LogLevel.Error, Message = "Provisioning step {Step} of tenant {TenantId} threw")]
    private partial void LogStepException(Exception ex, string tenantId, ProvisioningStep step);
}
