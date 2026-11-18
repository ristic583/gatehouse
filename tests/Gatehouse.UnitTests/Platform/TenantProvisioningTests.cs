using Gatehouse.Domain.Common;
using Gatehouse.Domain.Platform;
using Gatehouse.Domain.Tenancy;

namespace Gatehouse.UnitTests.Platform;

public sealed class TenantProvisioningTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 8, 0, 0, TimeSpan.Zero);

    private static TenantProvisioning NewProvisioning() =>
        TenantProvisioning.Start(new TenantId("acme"), "a@acme.test", "A", "B", "Europe/Copenhagen", Now);

    private static Tenant ActiveTenant() =>
        Tenant.Create(new TenantId("acme"), "Acme", IdpKind.Oidc, "v1:x", Now).Value;

    [Fact]
    public void Provisioning_advances_through_steps_in_order()
    {
        var p = NewProvisioning();
        var seen = new List<ProvisioningStep> { p.Step };
        while (p.State == ProvisioningState.Running)
        {
            Assert.True(p.Advance(Now).IsSuccess);
            seen.Add(p.Step);
        }

        Assert.Equal(
            [ProvisioningStep.Database, ProvisioningStep.Migrate, ProvisioningStep.Baseline, ProvisioningStep.FirstAdmin,
             ProvisioningStep.Organization, ProvisioningStep.AdminInvite, ProvisioningStep.Completed],
            seen);
        Assert.Equal(ProvisioningState.Completed, p.State);
        Assert.Equal(Now, p.CompletedAt);
        Assert.NotNull(p.AdminInvitedAt);
    }

    [Fact]
    public void Failed_step_can_be_retried()
    {
        var p = NewProvisioning();
        p.Fail("boom", Now);
        Assert.Equal(ProvisioningState.Failed, p.State);
        Assert.Equal(1, p.Attempts);

        Assert.True(p.Retry(Now).IsSuccess);
        Assert.Equal(ProvisioningState.Running, p.State);
        Assert.Equal(ProvisioningStep.Database, p.Step);
    }

    [Fact]
    public void Retry_when_not_failed_is_refused()
    {
        var result = NewProvisioning().Retry(Now);
        Assert.Equal("tenant.provisioning_not_failed", result.Error.Code);
    }

    [Fact]
    public void Suspend_requires_active_tenant()
    {
        var t = Tenant.CreateProvisioning(new TenantId("acme"), "Acme", "acme.test", Now).Value;
        Assert.Equal("tenant.invalid_status_transition", t.Suspend("r", Now).Error.Code);

        var active = ActiveTenant();
        Assert.True(active.Suspend("r", Now).IsSuccess);
        Assert.Equal(TenantStatus.Suspended, active.Status);
        Assert.Equal(Now, active.SuspendedAt);
    }

    [Fact]
    public void Reactivate_requires_suspended_tenant()
    {
        var t = ActiveTenant();
        Assert.Equal("tenant.invalid_status_transition", t.Reactivate().Error.Code);

        t.Suspend("r", Now);
        Assert.True(t.Reactivate().IsSuccess);
        Assert.Null(t.SuspendedReason);
    }

    [Fact]
    public void Last_error_is_truncated()
    {
        var p = NewProvisioning();
        p.Fail(new string('x', 5000), Now);
        Assert.Equal(TenantProvisioning.LastErrorMaxLength, p.LastError!.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("acme")]
    [InlineData("-acme.test")]
    [InlineData("acme..test")]
    [InlineData("user@acme.test")]
    public void Create_provisioning_rejects_invalid_email_domain(string domain)
    {
        var result = Tenant.CreateProvisioning(new TenantId("acme"), "Acme", domain, Now);
        Assert.Equal("tenant.invalid_email_domain", result.Error.Code);
    }

    [Fact]
    public void Create_provisioning_normalizes_email_domain()
    {
        var tenant = Tenant.CreateProvisioning(new TenantId("acme"), "Acme", " Acme-Corp.Test ", Now).Value;
        Assert.Equal("acme-corp.test", tenant.EmailDomain);
    }
}
