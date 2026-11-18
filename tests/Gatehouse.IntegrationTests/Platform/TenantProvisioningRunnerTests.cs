using System.Net;
using Gatehouse.Application.Platform.Provisioning;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Platform;
using Gatehouse.Domain.Tenancy;
using Gatehouse.Infrastructure.Persistence.Catalog;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.Infrastructure.Security;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Platform;

/// <summary>ADR-009 §3: resumable, idempotent provisioning steps.</summary>
public sealed class TenantProvisioningRunnerTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatehouseApiFactory _api = new(postgres);

    public ValueTask InitializeAsync() => _api.InitializeAsync();

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    private FakeIdentityProvisioner Identity => _api.Services.GetRequiredService<FakeIdentityProvisioner>();

    private ITenantProvisioningRunner Runner => _api.Services.GetRequiredService<ITenantProvisioningRunner>();

    [Fact]
    public async Task New_tenant_becomes_active_with_admin_and_levels()
    {
        var ct = TestContext.Current.CancellationToken;
        var (id, admin) = await StartAsync(ct);

        await Runner.RunOnceAsync(ct);

        var (tenant, provisioning) = await LoadAsync(id, ct);
        Assert.Equal(TenantStatus.Active, tenant.Status);
        Assert.Equal(ProvisioningState.Completed, provisioning.State);
        Assert.Equal(ProvisioningStep.Completed, provisioning.Step);
        Assert.NotNull(provisioning.AdminInvitedAt);
        Assert.True(Identity.Organizations.ContainsKey(id));
        Assert.True(Identity.Invites.ContainsKey((id, admin)));

        var counts = await TenantCountsAsync(id, admin, ct);
        Assert.Equal((4, 1, 1, 1), counts);

        await using var scope = _api.Services.CreateAsyncScope();
        var actions = await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().PlatformAuditEntries.AsNoTracking()
            .Where(e => e.TenantId == id).OrderBy(e => e.Sequence).Select(e => e.Action).ToListAsync(ct);
        Assert.Equal([ProvisioningActions.AdminInviteSent, ProvisioningActions.TenantProvisioned], actions);
    }

    [Fact]
    public async Task Failure_records_step_and_retry_resumes_there()
    {
        var ct = TestContext.Current.CancellationToken;
        var (id, admin) = await StartAsync(ct);
        Identity.FailNext(nameof(FakeIdentityProvisioner.EnsureOrganizationAsync));

        await Runner.RunOnceAsync(ct);

        var (tenant, provisioning) = await LoadAsync(id, ct);
        Assert.Equal(TenantStatus.ProvisioningFailed, tenant.Status);
        Assert.Equal(ProvisioningState.Failed, provisioning.State);
        Assert.Equal(ProvisioningStep.Organization, provisioning.Step);
        Assert.Equal(1, provisioning.Attempts);
        Assert.StartsWith("idp.provisioning_failed", provisioning.LastError, StringComparison.Ordinal);

        await using (var scope = _api.Services.CreateAsyncScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var p = await catalog.TenantProvisionings.SingleAsync(x => x.TenantId == id, ct);
            var t = await catalog.Tenants.SingleAsync(x => x.Id == id, ct);
            Assert.True(p.Retry(DateTimeOffset.UtcNow).IsSuccess);
            Assert.True(t.RetryProvisioning().IsSuccess);
            await catalog.SaveChangesAsync(ct);
        }

        var callsBefore = Identity.Calls.Count;
        await Runner.RunOnceAsync(ct);

        (tenant, provisioning) = await LoadAsync(id, ct);
        Assert.Equal(TenantStatus.Active, tenant.Status);
        Assert.Equal(ProvisioningState.Completed, provisioning.State);
        // Resumed at Organization: exactly EnsureOrganization + InviteTenantAdmin ran again.
        Assert.Equal(callsBefore + 2, Identity.Calls.Count);
        Assert.Equal((4, 1, 1, 1), await TenantCountsAsync(id, admin, ct));
    }

    [Fact]
    public async Task Rerun_after_partial_success_is_idempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        var (id, admin) = await StartAsync(ct);
        await Runner.RunOnceAsync(ct);

        // Simulate a crash after Baseline/FirstAdmin were written but before the step was stored.
        await using (var scope = _api.Services.CreateAsyncScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            await catalog.TenantProvisionings.Where(p => p.TenantId == id).ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Step, ProvisioningStep.Database)
                .SetProperty(p => p.State, ProvisioningState.Running)
                .SetProperty(p => p.CompletedAt, (DateTimeOffset?)null), ct);
            await catalog.Tenants.Where(t => t.Id == id).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, TenantStatus.Provisioning), ct);
        }

        await Runner.RunOnceAsync(ct);

        var (tenant, provisioning) = await LoadAsync(id, ct);
        Assert.Equal(TenantStatus.Active, tenant.Status);
        Assert.Equal(ProvisioningState.Completed, provisioning.State);
        Assert.Equal((4, 1, 1, 1), await TenantCountsAsync(id, admin, ct));
    }

    [Fact]
    public async Task Last_error_contains_no_connection_string()
    {
        var ct = TestContext.Current.CancellationToken;
        var (id, _) = await StartAsync(ct);
        const string Password = "s3cret-pw-never-logged"; // gitleaks:allow (test value)
        await using (var scope = _api.Services.CreateAsyncScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();
            var p = await catalog.TenantProvisionings.SingleAsync(x => x.TenantId == id, ct);
            var t = await catalog.Tenants.SingleAsync(x => x.Id == id, ct);
            Assert.True(p.Advance(DateTimeOffset.UtcNow).IsSuccess); // now at Migrate
            t.SetConnection(protector.Protect($"Host=127.0.0.1;Port=1;Database=nowhere;Username=ghost;Password={Password};Timeout=2"));
            await catalog.SaveChangesAsync(ct);
        }

        await Runner.RunOnceAsync(ct);

        var (_, provisioning) = await LoadAsync(id, ct);
        Assert.Equal(ProvisioningState.Failed, provisioning.State);
        Assert.Equal(ProvisioningStep.Migrate, provisioning.Step);
        Assert.NotNull(provisioning.LastError);
        Assert.DoesNotContain(Password, provisioning.LastError, StringComparison.Ordinal);
        Assert.DoesNotContain("Host=", provisioning.LastError, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("127.0.0.1", provisioning.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Created_tenant_resolves_for_its_admin_token()
    {
        var ct = TestContext.Current.CancellationToken;
        var (id, admin) = await StartAsync(ct);
        await Runner.RunOnceAsync(ct);

        using var client = _api.CreateClientFor($"kc-{Guid.NewGuid():N}", admin, id.Value);
        using var response = await client.GetAsync(new Uri("/api/v1/me", UriKind.Relative), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<(TenantId Id, string AdminEmail)> StartAsync(CancellationToken ct)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var id = new TenantId($"p{suffix}");
        var domain = $"{id.Value}.example";
        var admin = $"admin@{domain}";
        await using var scope = _api.Services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var now = DateTimeOffset.UtcNow;
        catalog.Tenants.Add(Tenant.CreateProvisioning(id, $"Tenant {suffix}", domain, now).Value);
        catalog.TenantProvisionings.Add(TenantProvisioning.Start(id, admin, "Ada", "Admin", "Europe/Copenhagen", now));
        await catalog.SaveChangesAsync(ct);
        return (id, admin);
    }

    private async Task<(Tenant Tenant, TenantProvisioning Provisioning)> LoadAsync(TenantId id, CancellationToken ct)
    {
        await using var scope = _api.Services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        return (await catalog.Tenants.AsNoTracking().SingleAsync(t => t.Id == id, ct),
            await catalog.TenantProvisionings.AsNoTracking().SingleAsync(p => p.TenantId == id, ct));
    }

    /// <summary>(security levels, admin persons, TenantAdmin assignments, tenant.provisioned audit entries).</summary>
    private async Task<(int, int, int, int)> TenantCountsAsync(TenantId id, string admin, CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync(id.Value);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var levels = await db.SecurityLevels.CountAsync(ct);
        var persons = await db.Persons.CountAsync(p => p.Email == admin && p.Type == PersonType.Employee && p.Source == PersonSource.Authoritative, ct);
        var roles = await db.RoleAssignments.CountAsync(r => r.Role == AssignedRole.TenantAdmin, ct);
        var audits = await db.AuditEntries.CountAsync(e => e.Action == ProvisioningActions.TenantChainProvisioned, ct);
        return (levels, persons, roles, audits);
    }
}
