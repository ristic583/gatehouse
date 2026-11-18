using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Aspire.Hosting.Testing;
using Gatehouse.Application.Identity;
using Gatehouse.Application.Identity.IdentityProviders;
using Gatehouse.Application.People.Dtos;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Tenancy;
using Gatehouse.IntegrationTests.Scenarios.Infrastructure;
using Npgsql;

namespace Gatehouse.IntegrationTests.Scenarios;

/// <summary>Scenarios 17, 20 (improvements.md §A2; GH-AUTH-05, GH-ADMIN-03) on tenants provisioned from the platform panel.</summary>
[Trait("Category", "AppHost")]
[Collection(ScenarioCollection.Name)]
public sealed class FreshTenantScenarios(ScenarioAppHostFixture fixture)
{
    [Fact]
    public async Task Concurrent_removal_of_last_two_admins_leaves_one_admin()
    {
        using var cts = ScenarioAppHostFixture.NewTestTimeout();
        var ct = cts.Token;
        using var tenant = await ScenarioTenantFactory.CreateWithAdminAsync(fixture, ct);
        var admin = tenant.Admin;
        var second = await admin.PostAsync<PersonSummaryDto>("v1/admin/people/employees",
            new CreateEmployeeRequest($"second.admin@{tenant.EmailDomain}", "Second", "Admin", null, null), ct);
        var secondRole = await admin.PostAsync<RoleAssignmentDto>("v1/admin/role-assignments",
            new AssignRoleRequest(second.PersonId, AssignedRole.TenantAdmin, null), ct);
        var roles = await admin.GetAsync<List<RoleAssignmentDto>>("v1/admin/role-assignments", ct);
        var ownRole = roles.Single(r => r.Role == AssignedRole.TenantAdmin && r.Person.PersonId != second.PersonId);

        var responses = await Task.WhenAll(
            admin.DeleteAsync($"v1/admin/role-assignments/{secondRole.Id}", ct),
            admin.DeleteAsync($"v1/admin/role-assignments/{ownRole.Id}", ct));
        try
        {
            Assert.Single(responses, r => r.IsSuccessStatusCode);
            var refused = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
            Assert.Contains("role.last_tenant_admin", await refused.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        // The remover may have removed its own role (then it can no longer list roles): count in the tenant database.
        Assert.Equal(1, await TenantAdminCountAsync(tenant.TenantId, ct));
    }

    [Fact]
    public async Task Failed_idp_kind_switch_keeps_previous_provider_working()
    {
        using var cts = ScenarioAppHostFixture.NewTestTimeout();
        var ct = cts.Token;
        using var tenant = await ScenarioTenantFactory.CreateWithAdminAsync(fixture, ct);
        var admin = tenant.Admin;
        var email = $"emp{tenant.TenantId["scn-".Length..]}@{tenant.EmailDomain}";
        await UpstreamIdp.CreateAcmeUserAsync(fixture, email, ct);
        await admin.PostAsync<PersonSummaryDto>(
            "v1/admin/people/employees", new CreateEmployeeRequest(email, "Scenario", "Upstream", null, null), ct);
        var secret = await fixture.ParameterAsync("acme-broker-client-secret", ct);
        var oidc = new SaveIdentityProviderRequest(
            IdpKind.Oidc, "Acme OIDC", new OidcSettingsRequest(UpstreamIdp.AcmeDiscoveryUrl, UpstreamIdp.AcmeClientId, secret, null), null);
        Assert.Equal(IdpKind.Oidc, (await admin.PutAsync<IdentityProviderDto>("v1/admin/identity-provider", oidc, ct)).Kind);
        await AssertUpstreamSignInAsync(tenant, email, ct);

        // Metadata Gatehouse accepts (entity, HTTP-POST SSO endpoint, readable signing certificate) but Keycloak refuses:
        // the SSO location is not an http(s) URL.
        var saml = new SaveIdentityProviderRequest(
            IdpKind.Saml, "Acme SAML", null, new SamlSettingsRequest(null, SamlMetadata(tenant.EmailDomain)));
        var test = await admin.PostAsync<IdpTestResultDto>("v1/admin/identity-provider/test", saml, ct);
        Assert.True(test.Ok, string.Join(", ", test.Checks.Where(c => !c.Ok).Select(c => c.Name)));
        using (var switched = await admin.PutAsync("v1/admin/identity-provider", saml, ct))
        {
            var body = await switched.Content.ReadAsStringAsync(ct);
            Assert.Equal(HttpStatusCode.BadGateway, switched.StatusCode);
            Assert.Contains("idp.provisioning_failed", body, StringComparison.Ordinal);
        }

        Assert.Equal(IdpKind.Oidc, (await admin.GetAsync<IdentityProviderDto>("v1/admin/identity-provider", ct)).Kind);
        await AssertUpstreamSignInAsync(tenant, email, ct);
    }

    private async Task AssertUpstreamSignInAsync(ScenarioTenant tenant, string email, CancellationToken ct)
    {
        using var user = new ScenarioClient(fixture.WebUrl, email, await fixture.ParameterAsync("demo-user-password", ct));
        await user.SignInAsync(ct);
        Assert.Equal(tenant.TenantId, (await user.GetAsync<MeDto>("v1/me", ct)).TenantId);
    }

    private async Task<long> TenantAdminCountAsync(string tenantId, CancellationToken ct)
    {
        var server = await fixture.App.GetConnectionStringAsync("postgres", ct);
        await using var db = NpgsqlDataSource.Create(
            new NpgsqlConnectionStringBuilder(server) { Database = new TenantId(tenantId).DatabaseName, MaxPoolSize = 1 }.ConnectionString);
        await using var command = db.CreateCommand("select count(*) from role_assignments where role = 'TenantAdmin'");
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }

    private static string SamlMetadata(string domain)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN=sso.{domain}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var der = Convert.ToBase64String(certificate.Export(X509ContentType.Cert));
        return $"""
            <md:EntityDescriptor xmlns:md="urn:oasis:names:tc:SAML:2.0:metadata" xmlns:ds="http://www.w3.org/2000/09/xmldsig#"
                entityID="https://sso.{domain}/saml">
              <md:IDPSSODescriptor protocolSupportEnumeration="urn:oasis:names:tc:SAML:2.0:protocol">
                <md:KeyDescriptor use="signing">
                  <ds:KeyInfo><ds:X509Data><ds:X509Certificate>{der}</ds:X509Certificate></ds:X509Data></ds:KeyInfo>
                </md:KeyDescriptor>
                <md:SingleSignOnService Binding="urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST" Location="ftp://sso.{domain}/saml"/>
              </md:IDPSSODescriptor>
            </md:EntityDescriptor>
            """;
    }
}
