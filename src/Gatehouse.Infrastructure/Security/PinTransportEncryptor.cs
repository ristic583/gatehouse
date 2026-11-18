using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Gatehouse.Application.Connectors;
using Gatehouse.Application.Credentials;

namespace Gatehouse.Infrastructure.Security;

/// <summary>Decrypts a <c>vault:</c> PIN reference and encrypts it with RSA-OAEP-SHA256 to the agent certificate; the plain PIN never
/// leaves memory.</summary>
public sealed class PinTransportEncryptor(IPinVault vault) : IPinTransportEncryptor
{
    public async Task<string?> EncryptForAgentAsync(string vaultReference, X509Certificate2 agentCertificate, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(agentCertificate);
        using var rsa = agentCertificate.GetRSAPublicKey();
        if (rsa is null)
        {
            return null;
        }

        var stored = vault.UnprotectTransport(vaultReference) ?? throw new CryptographicException("Not a vault PIN reference.");
        var pin = await vault.DecryptAsync(stored.Ciphertext, stored.KeyId, ct);
        var plain = Encoding.UTF8.GetBytes(pin.Reveal());
        try
        {
            return Convert.ToBase64String(rsa.Encrypt(plain, RSAEncryptionPadding.OaepSHA256));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }
}
