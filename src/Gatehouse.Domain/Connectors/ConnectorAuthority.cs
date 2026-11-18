namespace Gatehouse.Domain.Connectors;

public sealed class ConnectorAuthority
{
    private ConnectorAuthority(Guid id, string certificatePem, string encryptedPrivateKeyPem, DateTimeOffset createdAt)
    {
        Id = id;
        CertificatePem = certificatePem;
        EncryptedPrivateKeyPem = encryptedPrivateKeyPem;
        CreatedAt = createdAt;
    }

    // EF Core
    private ConnectorAuthority()
    {
        CertificatePem = string.Empty;
        EncryptedPrivateKeyPem = string.Empty;
    }

    public Guid Id { get; private set; }

    public string CertificatePem { get; private set; }

    public string EncryptedPrivateKeyPem { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static ConnectorAuthority Create(string certificatePem, string encryptedPrivateKeyPem, DateTimeOffset now) =>
        new(Guid.CreateVersion7(), certificatePem, encryptedPrivateKeyPem, now);
}
