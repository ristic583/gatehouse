using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Gatehouse.Agent.Execution;

namespace Gatehouse.UnitTests.Credentials;

public sealed class CertificatePinDecryptorTests
{
    [Fact]
    public void Decryptor_reads_rsa_oaep_ciphertext()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=gatehouse-agent", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var publicOnly = X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert));
        using var publicKey = publicOnly.GetRSAPublicKey()!;
        var encrypted = Convert.ToBase64String(publicKey.Encrypt(Encoding.UTF8.GetBytes("2468"), RSAEncryptionPadding.OaepSHA256));

        var pin = new CertificatePinDecryptor(certificate).Decrypt(encrypted);

        Assert.Equal("2468", pin.Reveal());
        Assert.DoesNotContain("2468", pin.ToString() ?? string.Empty, StringComparison.Ordinal);
    }
}
