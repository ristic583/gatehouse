using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;

namespace Gatehouse.IntegrationTests.Identity;

public sealed class LinkedPersonPolicyTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatehouseApiFactory _factory = new(postgres);

    public ValueTask InitializeAsync() => _factory.InitializeAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Unprovisioned_user_gets_403_with_code()
    {
        var ct = TestContext.Current.CancellationToken;
        // The dev reset endpoint requires TenantAdmin, which includes LinkedPerson.
        await using var app = _factory.WithWebHostBuilder(b => b.UseSetting("Gatehouse:Dev:EnableReset", "true"));
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, "test-stranger");
        client.DefaultRequestHeaders.Add(TestAuthHandler.EmailHeader, "stranger@nordlys.test");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, "nordlys");

        using var response = await client.PostAsync(new Uri("/api/v1/dev/reset-demo-access", UriKind.Relative), null, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("identity.person_not_provisioned", body.GetProperty("code").GetString());
    }
}
