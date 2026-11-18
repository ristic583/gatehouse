using System.Net;
using Aspire.Hosting;
using Aspire.Hosting.Testing;

namespace Gatehouse.IntegrationTests.AppHost;

[Trait("Category", "AppHost")]
[Collection(AppHostCollection.Name)]
public sealed class KeycloakAppHostTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task Realms_publish_metadata()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(Timeout);

        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Gatehouse_AppHost>(
            ["DcpPublisher:RandomizePorts=false"],
            cts.Token);
        await using var app = await builder.BuildAsync(cts.Token);
        await app.StartAsync(cts.Token);

        await app.ResourceNotifications.WaitForResourceHealthyAsync("upstream-idp", cts.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("keycloak", cts.Token);

        using var http = new HttpClient();
        string[] urls =
        [
            "http://localhost:8080/realms/gatehouse/.well-known/openid-configuration",
            "http://localhost:8080/realms/gatehouse/broker/bricks-saml/endpoint/descriptor",
            "http://localhost:8081/realms/nordlys-oidc/.well-known/openid-configuration",
            "http://localhost:8081/realms/bricks-saml/protocol/saml/descriptor",
        ];

        foreach (var url in urls)
        {
            using var response = await http.GetAsync(new Uri(url), cts.Token);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url} returned {(int)response.StatusCode}");
        }
    }
}
