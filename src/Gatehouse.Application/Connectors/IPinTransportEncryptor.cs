using System.Security.Cryptography.X509Certificates;

namespace Gatehouse.Application.Connectors;

/// <summary>Gateway side of <c>SetPin</c>: turns a stored <c>vault:</c> reference into RSA-OAEP-SHA256 ciphertext for the polling
/// agent.</summary>
public interface IPinTransportEncryptor
{
    /// <summary>Base64 ciphertext to the certificate's RSA public key, or null when the certificate has no RSA key.</summary>
    public Task<string?> EncryptForAgentAsync(string vaultReference, X509Certificate2 agentCertificate, CancellationToken ct);
}
