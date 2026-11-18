using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Protocol;
using Microsoft.Extensions.Logging;

namespace Gatehouse.Agent.Transport;

public sealed partial class EnrollmentService(AgentOptions options, ILogger<EnrollmentService> logger)
{
    public string PfxPath => Path.Combine(options.DataDirectory, "client.pfx");

    public bool IsEnrolled => File.Exists(PfxPath);

    public async Task EnrollAsync(CancellationToken ct)
    {
        if (IsEnrolled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(options.EnrollmentToken))
        {
            throw new InvalidOperationException("Agent is not enrolled and no Agent:EnrollmentToken is configured.");
        }

        Directory.CreateDirectory(options.DataDirectory);
        using var rsa = RSA.Create(3072);
        var csr = new CertificateRequest(
            "CN=gatehouse-agent", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).CreateSigningRequestPem();

        using var http = ClientFactory.CreateHandlerClient(options, null);
        using var message = new HttpRequestMessage(HttpMethod.Post, "connector/v1/enroll")
        {
            Content = JsonContent.Create(
                new EnrollRequest(options.EnrollmentToken, csr, options.AgentVersion), options: ConnectorJson.Options),
        };
        message.Headers.Add(ConnectorHeaders.ContractVersion, ConnectorHeaders.CurrentContractVersion);
        message.Headers.Add(ConnectorHeaders.AgentVersion, options.AgentVersion);
        using var response = await http.SendAsync(message, ct);
        response.EnsureSuccessStatusCode();
        var enrolled = (await response.Content.ReadFromJsonAsync<EnrollResponse>(ConnectorJson.Options, ct))!;

        using var cert = X509Certificate2.CreateFromPem(enrolled.CertificatePem).CopyWithPrivateKey(rsa);
        // The private key is never world-readable, not even briefly: the file is created with mode 0600.
        var pfxOptions = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            pfxOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        await using (var pfx = new FileStream(PfxPath, pfxOptions))
        {
            await pfx.WriteAsync(cert.Export(X509ContentType.Pfx), ct);
        }

        if (!OperatingSystem.IsWindows())
        {
            // UnixCreateMode applies only to new files; an existing file from an earlier enrollment keeps its mode otherwise.
            File.SetUnixFileMode(PfxPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        await File.WriteAllTextAsync(Path.Combine(options.DataDirectory, "ca.pem"), enrolled.CaChainPem, ct);

        LogEnrolled(enrolled.ConnectorId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent enrolled as connector {ConnectorId}")]
    private partial void LogEnrolled(Guid connectorId);
}
