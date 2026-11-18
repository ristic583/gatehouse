using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Gatehouse.Connector.Contracts.Adapters;

namespace Gatehouse.Agent.Execution;

/// <summary>Decrypts <c>SetPin.EncryptedPin</c> (base64 RSA-OAEP-SHA256) with the enrolled certificate's private key.</summary>
public sealed class CertificatePinDecryptor(X509Certificate2 certificate) : IPinDecryptor
{
    public SensitivePin Decrypt(string encryptedPin)
    {
        using var rsa = certificate.GetRSAPrivateKey() ?? throw new CryptographicException("The agent certificate has no RSA private key.");
        var plain = rsa.Decrypt(Convert.FromBase64String(encryptedPin), RSAEncryptionPadding.OaepSHA256);
        try
        {
            return new SensitivePin(Encoding.UTF8.GetString(plain));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }
}
