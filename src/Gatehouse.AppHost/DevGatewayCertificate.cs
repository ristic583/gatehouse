using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Gatehouse.AppHost;

/// <summary>
/// Self-signed TLS certificate for the local connector gateway. The ASP.NET dev certificate lives in the macOS
/// keychain, which child processes started by DCP cannot always read; a password-less PFX file avoids that.
/// The dev agent accepts any gateway certificate (<c>Agent__TrustGatewayCertificate=dev</c>).
/// </summary>
internal static class DevGatewayCertificate
{
    public static string EnsureCreated()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gatehouse", "dev-certs");
        var path = Path.Combine(directory, "gateway.pfx");
        if (File.Exists(path))
        {
            return path;
        }

        Directory.CreateDirectory(directory);
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(System.Net.IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], critical: false));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx));
        return path;
    }

    public static async Task<HealthCheckResult> CheckHealthAsync(EndpointReference endpoint, string certificatePath, CancellationToken ct)
    {
        using var expected = X509CertificateLoader.LoadPkcs12FromFile(certificatePath, password: null);
        var thumbprint = expected.Thumbprint;
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) => certificate?.Thumbprint == thumbprint,
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            using var response = await client.GetAsync(new Uri(new Uri(endpoint.Url), "/health"), ct);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"Gateway /health returned {(int)response.StatusCode}.");
        }
        catch (HttpRequestException exception)
        {
            return HealthCheckResult.Unhealthy("Gateway /health is not reachable.", exception);
        }
        catch (TaskCanceledException exception) when (!ct.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("Gateway /health timed out.", exception);
        }
    }
}
