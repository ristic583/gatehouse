using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Infrastructure.Connectors;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Connectors;

public sealed class ConnectorCertificateAuthorityTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatehouseApiFactory _factory = new(postgres);

    public ValueTask InitializeAsync() => _factory.InitializeAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Issued_certificate_chains_to_ca_and_has_san()
    {
        var ct = TestContext.Current.CancellationToken;
        using var rsa = RSA.Create(3072);
        var csr = new CertificateRequest("CN=ignored", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).CreateSigningRequestPem();
        var tenant = new TenantId("nordlys");
        var pacs = PacsInstanceId.New();
        var connector = ConnectorId.New();
        await using var scope = _factory.Services.CreateAsyncScope();
        var ca = scope.ServiceProvider.GetRequiredService<ConnectorCertificateAuthority>();
        var result = await ca.IssueClientCertificateAsync(csr, tenant, pacs, connector, DateTimeOffset.UtcNow, ct);
        Assert.True(result.IsSuccess);

        using var leaf = X509Certificate2.CreateFromPem(result.Value.CertificatePem);
        using var root = X509Certificate2.CreateFromPem(result.Value.CaPem);
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(root);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        Assert.True(chain.Build(leaf));
        Assert.Equal($"CN=gatehouse-connector-{connector.Value}", leaf.Subject);
        var san = leaf.Extensions.Single(e => e.Oid?.Value == "2.5.29.17");
        Assert.Contains($"urn:gatehouse:tenant:nordlys:pacs:{pacs.Value}:connector:{connector.Value}", san.Format(false), StringComparison.Ordinal);
        Assert.Equal(result.Value.ThumbprintSha256Hex, Convert.ToHexString(leaf.GetCertHash(HashAlgorithmName.SHA256)).ToLowerInvariant());
    }

    [Fact]
    public async Task Issue_right_after_ca_creation_with_earlier_clock_succeeds()
    {
        // Regression (M9-15): the enroll-500 flake. The CA's NotBefore was later than the client's backdated notBefore.
        var ct = TestContext.Current.CancellationToken;
        using var rsa = RSA.Create(3072);
        var csr = new CertificateRequest("CN=ignored", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).CreateSigningRequestPem();
        await using var scope = _factory.Services.CreateAsyncScope();
        var ca = scope.ServiceProvider.GetRequiredService<ConnectorCertificateAuthority>();
        await ca.IssueClientCertificateAsync(csr, new TenantId("nordlys"), PacsInstanceId.New(), ConnectorId.New(), DateTimeOffset.UtcNow, ct);

        var result = await ca.IssueClientCertificateAsync(csr, new TenantId("nordlys"), PacsInstanceId.New(), ConnectorId.New(), DateTimeOffset.UtcNow.AddHours(-1), ct);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Rejects_rsa_2048_csr()
    {
        var ct = TestContext.Current.CancellationToken;
        using var rsa = RSA.Create(2048);
        var csr = new CertificateRequest("CN=x", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).CreateSigningRequestPem();
        await using var scope = _factory.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<ConnectorCertificateAuthority>()
            .IssueClientCertificateAsync(csr, new TenantId("nordlys"), PacsInstanceId.New(), ConnectorId.New(), DateTimeOffset.UtcNow, ct);
        Assert.True(result.IsFailure);
        Assert.Equal("connector.csr_invalid", result.Error.Code);
    }
}
