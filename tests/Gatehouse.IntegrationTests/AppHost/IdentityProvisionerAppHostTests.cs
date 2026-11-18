using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Gatehouse.Application.Tenancy.IdentityProvisioning;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tenancy;
using Gatehouse.Infrastructure.Identity.Keycloak;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Gatehouse.IntegrationTests.AppHost;

[Trait("Category", "AppHost")]
[Collection(AppHostCollection.Name)]
public sealed class IdentityProvisionerAppHostTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task Provisioner_creates_organization_invites_admin_and_links_oidc_provider()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(Timeout);
        var ct = cts.Token;

        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Gatehouse_AppHost>(["DcpPublisher:RandomizePorts=false"], ct);
        await using var app = await builder.BuildAsync(ct);
        await app.StartAsync(ct);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("keycloak", ct);

        var secret = await app.Services.GetRequiredService<DistributedApplicationModel>().Resources.OfType<ParameterResource>()
            .Single(p => p.Name == "provisioner-client-secret").GetValueAsync(ct);
        var options = Options.Create(new KeycloakOptions { ProvisionerClientSecret = secret });
        using var http = new HttpClient();
        using var tokens = new KeycloakAdminTokenCache(options, TimeProvider.System);
        var provisioner = new KeycloakIdentityProvisioner(http, tokens, options, NullLogger<KeycloakIdentityProvisioner>.Instance);

        var tenant = new TenantId($"acme{Guid.NewGuid():N}"[..20]);
        var domain = $"{tenant.Value}.test";
        var adminEmail = $"admin@{domain}";

        var created = await provisioner.EnsureOrganizationAsync(tenant, "Acme Corp", domain, ct);
        Assert.True(created.IsSuccess, created.IsFailure ? $"{created.Error.Code}: {created.Error.Message}" : null);
        Assert.True((await provisioner.EnsureOrganizationAsync(tenant, "Acme Corp", domain, ct)).IsSuccess);
        Assert.Equal("tenant.email_domain_taken", (await provisioner.EnsureOrganizationAsync(new TenantId($"{tenant.Value}x"), "Other", domain, ct)).Error.Code);
        var invited = await provisioner.InviteTenantAdminAsync(tenant, adminEmail, "Ada", "Admin", ct);
        Assert.True(invited.IsSuccess, invited.IsFailure ? $"{invited.Error.Code}: {invited.Error.Message}" : null);
        Assert.False((await provisioner.HasSignedInAsync(tenant, adminEmail, ct)).Value);

        const string Upstream = "http://localhost:8081/realms/acme-oidc";
        var provider = await provisioner.UpsertOidcProviderAsync(tenant, new OidcProviderSpec(
            "Acme Corp", Upstream, $"{Upstream}/protocol/openid-connect/auth", $"{Upstream}/protocol/openid-connect/token",
            $"{Upstream}/protocol/openid-connect/certs", null, "gatehouse-broker-acme", "not-the-real-secret", ["openid", "profile", "email"]), ct);
        Assert.True(provider.IsSuccess, provider.IsFailure ? provider.Error.Message : null);
        Assert.Equal($"{tenant.Value}-oidc", (await provisioner.GetProviderAsync(tenant, ct)).Value!.Alias);

        JsonElement messages = default;
        for (var i = 0; i < 20; i++)
        {
            messages = await http.GetFromJsonAsync<JsonElement>(new Uri($"http://localhost:8025/api/v1/search?query=to:{Uri.EscapeDataString(adminEmail)}"), ct);
            if (messages.GetProperty("messages_count").GetInt32() > 0)
            {
                break;
            }

            await Task.Delay(500, ct);
        }

        Assert.True(messages.GetProperty("messages_count").GetInt32() > 0, "No invitation mail in Mailpit.");
        Assert.True((await provisioner.RemoveProviderAsync(tenant, ct)).IsSuccess);
        Assert.Equal(IdpKind.Oidc, provider.Value.Kind);
    }
}
