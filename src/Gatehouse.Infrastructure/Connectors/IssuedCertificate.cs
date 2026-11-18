namespace Gatehouse.Infrastructure.Connectors;

public sealed record IssuedCertificate(string CertificatePem, string CaPem, string ThumbprintSha256Hex, DateTimeOffset NotAfter);
