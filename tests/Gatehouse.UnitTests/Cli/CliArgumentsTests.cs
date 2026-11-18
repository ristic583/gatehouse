using Gatehouse.Cli;
using Gatehouse.Domain.Tenancy;

namespace Gatehouse.UnitTests.Cli;

public sealed class CliArgumentsTests
{
    [Fact]
    public void Bootstrap_production_skips_demo_data()
    {
        Assert.Equal(new CliCommand.Bootstrap(Demo: true), CliArguments.Parse(["bootstrap"]).Value);
        Assert.Equal(new CliCommand.Bootstrap(Demo: false), CliArguments.Parse(["bootstrap", "--production"]).Value);
    }

    [Fact]
    public void Parses_purge_test_tenants_list_only_unless_apply()
    {
        Assert.Equal(new CliCommand.PurgeTestTenants(Apply: false), CliArguments.Parse(["purge-test-tenants"]).Value);
        Assert.Equal(new CliCommand.PurgeTestTenants(Apply: true), CliArguments.Parse(["purge-test-tenants", "--apply"]).Value);
    }

    [Theory]
    [InlineData("e2e-1731768000000", true)]
    [InlineData("scn-173176800000042", true)]
    [InlineData("nordlys", false)]
    [InlineData("acme-e2e-1", false)]
    public void Purge_only_matches_e2e_and_scenario_tenants(string tenantId, bool expected) =>
        Assert.Equal(expected, TestTenantPurge.IsTestTenant(tenantId));

    [Fact]
    public void Parses_migrate_all()
    {
        var result = CliArguments.Parse(["migrate", "--all"]);

        Assert.True(result.IsSuccess);
        Assert.IsType<CliCommand.MigrateAll>(result.Value);
    }

    [Fact]
    public void Parses_tenant_create()
    {
        var result = CliArguments.Parse(["tenant", "create", "--id", "nordlys", "--name", "Nordlys A/S", "--idp", "oidc"]);

        var command = Assert.IsType<CliCommand.CreateTenant>(result.Value);
        Assert.Equal("nordlys", command.TenantId.Value);
        Assert.Equal("Nordlys A/S", command.DisplayName);
        Assert.Equal(IdpKind.Oidc, command.IdpKind);
    }

    [Fact]
    public void Unknown_command_is_usage_error()
    {
        var result = CliArguments.Parse(["frobnicate"]);

        Assert.True(result.IsFailure);
        Assert.Equal(CliArguments.UsageErrorCode, result.Error.Code);
    }

    [Fact]
    public void Tenant_create_without_id_is_usage_error()
    {
        var result = CliArguments.Parse(["tenant", "create", "--name", "Nordlys A/S", "--idp", "oidc"]);

        Assert.True(result.IsFailure);
        Assert.Equal(CliArguments.UsageErrorCode, result.Error.Code);
    }

    [Fact]
    public void Parses_seed_with_reset_access()
    {
        var plain = Assert.IsType<CliCommand.Seed>(CliArguments.Parse(["seed", "--tenant", "nordlys"]).Value);
        Assert.False(plain.ResetAccess);

        var reset = Assert.IsType<CliCommand.Seed>(CliArguments.Parse(["seed", "--tenant", "bricks", "--reset-access"]).Value);
        Assert.Equal("bricks", reset.TenantId.Value);
        Assert.True(reset.ResetAccess);
    }
}
