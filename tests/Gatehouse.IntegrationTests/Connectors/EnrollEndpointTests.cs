using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Protocol;
using Gatehouse.Domain.Common;
using Gatehouse.Infrastructure.Connectors;
using Gatehouse.Infrastructure.Persistence.Catalog;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Connectors;

public sealed class EnrollEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatewayFactory _factory = new(postgres);

    public async ValueTask InitializeAsync()
    {
        await _factory.InitializeAsync();
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().ConnectorRegistrations.ExecuteDeleteAsync(CancellationToken.None);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private async Task<string> CreateTokenAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<ConnectorRegistrationService>().CreateAsync(new TenantId("nordlys"), "HQ-Copenhagen", TestContext.Current.CancellationToken);
        return result.Value;
    }

    private Task<HttpResponseMessage> PostEnrollAsync(string token, bool withVersion = true)
    {
        var (csr, _) = ConnectorTestEnrollment.CreateCsr();
        var request = new HttpRequestMessage(HttpMethod.Post, "/connector/v1/enroll") { Content = JsonContent.Create(new EnrollRequest(token, csr, "1.0.0"), options: ConnectorJson.Options) };
        if (withVersion)
        {
            request.Headers.Add(ConnectorHeaders.ContractVersion, ConnectorHeaders.CurrentContractVersion);
        }

        return _factory.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return doc.RootElement.GetProperty("code").GetString();
    }

    [Fact]
    public async Task Valid_token_returns_certificate()
    {
        var response = await PostEnrollAsync(await CreateTokenAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<EnrollResponse>(ConnectorJson.Options, TestContext.Current.CancellationToken);
        Assert.StartsWith("-----BEGIN CERTIFICATE-----", body!.CertificatePem, StringComparison.Ordinal);
        Assert.Equal("nordlys", body.TenantId);
    }

    [Fact]
    public async Task Reused_token_returns_401_enrollment_invalid()
    {
        var token = await CreateTokenAsync();
        Assert.Equal(HttpStatusCode.OK, (await PostEnrollAsync(token)).StatusCode);
        var second = await PostEnrollAsync(token);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Equal("connector.enrollment_invalid", await CodeAsync(second));
    }

    [Fact]
    public async Task Expired_token_returns_401_enrollment_expired()
    {
        var token = await CreateTokenAsync();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().ConnectorRegistrations
                .ExecuteUpdateAsync(r => r.SetProperty(x => x.EnrollmentTokenExpiresAt, DateTimeOffset.UtcNow.AddHours(-1)), TestContext.Current.CancellationToken);
        }

        var response = await PostEnrollAsync(token);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("connector.enrollment_expired", await CodeAsync(response));
    }

    [Fact]
    public async Task Missing_contract_version_returns_400()
    {
        var response = await PostEnrollAsync("ghe_x", withVersion: false);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("connector.contract_version_unsupported", await CodeAsync(response));
    }
}
