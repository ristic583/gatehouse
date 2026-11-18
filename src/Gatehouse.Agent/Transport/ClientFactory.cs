using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Gatehouse.Agent.Transport;

public static class ClientFactory
{
    public static HttpClient CreateHandlerClient(AgentOptions options, X509Certificate2? clientCertificate)
    {
        var handler = new HttpClientHandler();
        if (clientCertificate is not null)
        {
            handler.ClientCertificates.Add(clientCertificate);
        }

        if (!string.IsNullOrWhiteSpace(options.GatewayCertificatePath))
        {
            var trusted = X509CertificateLoader.LoadCertificateFromFile(options.GatewayCertificatePath);
            handler.ServerCertificateCustomValidationCallback = (_, certificate, _, errors) => IsTrusted(certificate, errors, trusted);
        }
        else if (options.TrustDevGatewayCertificate)
        {
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        return new HttpClient(handler, true)
        {
            BaseAddress = new Uri(options.GatewayUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(60),
        };
    }

    /// <summary>
    /// <c>Agent:GatewayCertificatePath</c>: the gateway's certificate (or the CA that issued it) is the only trust root. The operating
    /// system's trust store is ignored, so a certificate it trusts (TLS-inspection proxy, mis-issued public certificate) is refused
    /// unless it chains to the pin (PT-35). Host name and validity are still checked.
    /// </summary>
    public static bool IsTrusted(X509Certificate2? certificate, SslPolicyErrors errors, X509Certificate2 trusted)
    {
        ArgumentNullException.ThrowIfNull(trusted);
        if (certificate is null || (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != SslPolicyErrors.None)
        {
            return false;
        }

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(trusted);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        return chain.Build(certificate);
    }
}
