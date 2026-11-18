using Gatehouse.Domain.Credentials;

namespace Gatehouse.Application.Credentials;

/// <summary>Envelope encryption for PINs at rest (local KMS today; AWS/Azure KMS providers can implement the same contract).</summary>
public interface IPinVault
{
    public const string TransportPrefix = "vault:";

    public Task<(byte[] Ciphertext, string KeyId)> EncryptAsync(Pin pin, CancellationToken ct);

    public Task<Pin> DecryptAsync(byte[] ciphertext, string keyId, CancellationToken ct);

    /// <summary><c>vault:{base64(ciphertext)}:{keyId}</c>, the stored <c>SetPin.EncryptedPin</c> until the gateway re-encrypts it for the
    /// agent.</summary>
    public string ProtectForTransport(byte[] ciphertext, string keyId);

    /// <summary>The inverse of <see cref="ProtectForTransport"/>; null when the value is not a vault reference.</summary>
    public (byte[] Ciphertext, string KeyId)? UnprotectTransport(string value);
}
