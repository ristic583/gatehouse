using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Infrastructure.Persistence.Catalog;
using Gatehouse.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Infrastructure.Connectors;

/// <summary>Gatehouse's own connector CA: one self-signed RSA 3072 root; issues 90-day client certificates (connector-protocol
/// §1).</summary>
public sealed class ConnectorCertificateAuthority(CatalogDbContext catalog, ISecretProtector protector, TimeProvider time)
{
    public const int RequiredKeySize = 3072;
    public static readonly TimeSpan ClientCertificateLifetime = TimeSpan.FromDays(90);

    public async Task EnsureCreatedAsync(CancellationToken ct)
    {
        if (await catalog.ConnectorAuthorities.AnyAsync(ct))
        {
            return;
        }

        var now = time.GetUtcNow();
        using var rsa = RSA.Create(RequiredKeySize);
        var request = new CertificateRequest("CN=Gatehouse Connector CA", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        using var cert = request.CreateSelfSigned(now.AddMinutes(-5), now.AddYears(10));
        catalog.ConnectorAuthorities.Add(
            ConnectorAuthority.Create(cert.ExportCertificatePem(), protector.Protect(rsa.ExportPkcs8PrivateKeyPem()), now));
        await catalog.SaveChangesAsync(ct);
    }

    public async Task<string> GetCaCertificateAsync(CancellationToken ct)
    {
        await EnsureCreatedAsync(ct);
        return (await catalog.ConnectorAuthorities.AsNoTracking().OrderBy(a => a.CreatedAt).FirstAsync(ct)).CertificatePem;
    }

    public async Task<Result<IssuedCertificate>> IssueClientCertificateAsync(
        string csrPem,
        TenantId tenantId,
        PacsInstanceId pacsInstanceId,
        ConnectorId connectorId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        // PT-32: the CSR signature is verified (proof of possession of the private key); a broken one is connector.csr_invalid.
        CertificateRequest csr;
        try
        {
            csr = CertificateRequest.LoadSigningRequestPem(
                csrPem, HashAlgorithmName.SHA256, CertificateRequestLoadOptions.UnsafeLoadCertificateExtensions);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            return Error.Validation("connector.csr_invalid", "The certificate signing request is invalid.");
        }

        using var publicKey = csr.PublicKey.GetRSAPublicKey();
        if (publicKey is null || publicKey.KeySize != RequiredKeySize)
        {
            return Error.Validation("connector.csr_invalid", "The CSR must use an RSA 3072 key.");
        }

        await EnsureCreatedAsync(ct);
        var authority = await catalog.ConnectorAuthorities.AsNoTracking().OrderBy(a => a.CreatedAt).FirstAsync(ct);
        using var ca = X509Certificate2.CreateFromPem(authority.CertificatePem, protector.Unprotect(authority.EncryptedPrivateKeyPem));
        var request = new CertificateRequest(
            new X500DistinguishedName($"CN=gatehouse-connector-{connectorId.Value}"), csr.PublicKey, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddUri(new Uri($"urn:gatehouse:tenant:{tenantId.Value}:pacs:{pacsInstanceId.Value}:connector:{connectorId.Value}"));
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.2")], true));
        var notAfter = now + ClientCertificateLifetime;
        // A CA created in the same second (or by a host whose clock runs slightly ahead) must not reject the backdated notBefore:
        // clamp the client validity into the issuer's validity.
        var caNotBefore = new DateTimeOffset(ca.NotBefore.ToUniversalTime(), TimeSpan.Zero);
        var notBefore = now.AddMinutes(-5) < caNotBefore ? caNotBefore : now.AddMinutes(-5);
        using var issued = request.Create(ca, notBefore, notAfter, RandomNumberGenerator.GetBytes(16));
        return new IssuedCertificate(
            issued.ExportCertificatePem(),
            authority.CertificatePem,
            Convert.ToHexString(issued.GetCertHash(HashAlgorithmName.SHA256)).ToLowerInvariant(),
            notAfter);
    }
}
