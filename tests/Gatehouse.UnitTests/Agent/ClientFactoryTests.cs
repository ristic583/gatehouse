using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Gatehouse.Agent.Transport;

namespace Gatehouse.UnitTests.Agent;

/// <summary><c>Agent:GatewayCertificatePath</c>: only the pinned gateway certificate is trusted, and host-name errors still fail.</summary>
public sealed class ClientFactoryTests
{
    [Fact]
    public void Pinned_certificate_is_trusted()
    {
        using var gateway = SelfSigned("gateway");

        Assert.True(ClientFactory.IsTrusted(gateway, SslPolicyErrors.RemoteCertificateChainErrors, gateway));
        Assert.True(ClientFactory.IsTrusted(gateway, SslPolicyErrors.None, gateway));
    }

    [Fact]
    public void Other_certificate_is_refused()
    {
        using var gateway = SelfSigned("gateway");
        using var impostor = SelfSigned("gateway");

        Assert.False(ClientFactory.IsTrusted(impostor, SslPolicyErrors.RemoteCertificateChainErrors, gateway));
    }

    [Fact]
    public void Host_name_mismatch_is_refused_even_for_the_pinned_certificate()
    {
        using var gateway = SelfSigned("gateway");

        Assert.False(ClientFactory.IsTrusted(gateway, SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateChainErrors, gateway));
        Assert.False(ClientFactory.IsTrusted(null, SslPolicyErrors.RemoteCertificateChainErrors, gateway));
    }

    /// <summary>
    /// Pentest run 3 (M11-25): with <c>Agent:GatewayCertificatePath</c> set, a gateway impersonator whose certificate the operating
    /// system trusts (corporate TLS-inspection CA, mis-issued public certificate) must still be refused.
    /// </summary>
    [Fact]
    public void Unpinned_gateway_certificate_is_refused()
    {
        using var gateway = SelfSigned("gateway");
        using var impostor = SelfSigned("gateway");

        Assert.False(ClientFactory.IsTrusted(impostor, SslPolicyErrors.None, gateway));
        Assert.False(ClientFactory.IsTrusted(impostor, SslPolicyErrors.RemoteCertificateChainErrors, gateway));
    }

    private static X509Certificate2 SelfSigned(string name)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
    }
}
