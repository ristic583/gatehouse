using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Gatehouse.Infrastructure.Security;

/// <summary>AES-256-GCM protection for secrets at rest (tenant connection strings).</summary>
public sealed class AesGcmSecretProtector : ISecretProtector
{
    public const string MasterKeySetting = "Gatehouse:Secrets:MasterKey";
    public const int MinimumMasterKeyLength = 32;

    private const string Prefix = "v1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    public AesGcmSecretProtector(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var masterKey = configuration[MasterKeySetting];
        if (string.IsNullOrEmpty(masterKey) || masterKey.Length < MinimumMasterKeyLength)
        {
            throw new InvalidOperationException(
                $"Configuration '{MasterKeySetting}' is missing or shorter than {MinimumMasterKeyLength} characters.");
        }

        _key = SHA256.HashData(Encoding.UTF8.GetBytes(masterKey));
    }

    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var payload = new byte[NonceSize + plainBytes.Length + TagSize];
        var nonce = payload.AsSpan(0, NonceSize);
        var cipher = payload.AsSpan(NonceSize, plainBytes.Length);
        var tag = payload.AsSpan(NonceSize + plainBytes.Length, TagSize);

        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        return Prefix + Convert.ToBase64String(payload);
    }

    public string Unprotect(string protectedValue)
    {
        ArgumentNullException.ThrowIfNull(protectedValue);

        if (!protectedValue.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new CryptographicException("Unknown protected value format.");
        }

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(protectedValue[Prefix.Length..]);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("Protected value is not valid base64.", ex);
        }

        if (payload.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Protected value is too short.");
        }

        var cipherLength = payload.Length - NonceSize - TagSize;
        var plainBytes = new byte[cipherLength];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(
            payload.AsSpan(0, NonceSize),
            payload.AsSpan(NonceSize, cipherLength),
            payload.AsSpan(NonceSize + cipherLength, TagSize),
            plainBytes);

        return Encoding.UTF8.GetString(plainBytes);
    }
}
