using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Gatehouse.Application.Credentials;
using Gatehouse.Domain.Credentials;
using Microsoft.Extensions.Configuration;

namespace Gatehouse.Infrastructure.Security;

/// <summary>
/// Local KMS for PINs: a random 256-bit data key per PIN (AES-256-GCM), wrapped with a KEK derived from
/// <c>Gatehouse:Secrets:MasterKey</c> (HKDF-SHA256, info <c>gatehouse-pin-kek-v1</c>).
/// Format: <c>[wrappedKeyLen:int32 BE][wrappedKey = nonce|tag|key][nonce][tag][ciphertext]</c>.
/// </summary>
public sealed class LocalPinVault : IPinVault
{
    public const string LocalKeyId = "local-v1";

    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly byte[] KekInfo = Encoding.UTF8.GetBytes("gatehouse-pin-kek-v1");

    private readonly byte[] _kek;

    public LocalPinVault(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var masterKey = configuration[AesGcmSecretProtector.MasterKeySetting];
        if (string.IsNullOrEmpty(masterKey) || masterKey.Length < AesGcmSecretProtector.MinimumMasterKeyLength)
        {
            throw new InvalidOperationException(
                $"Configuration '{AesGcmSecretProtector.MasterKeySetting}' is missing or shorter than "
                + $"{AesGcmSecretProtector.MinimumMasterKeyLength} characters.");
        }

        _kek = HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(masterKey), KeySize, salt: [], info: KekInfo);
    }

    public Task<(byte[] Ciphertext, string KeyId)> EncryptAsync(Pin pin, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(pin);
        var dataKey = RandomNumberGenerator.GetBytes(KeySize);
        var plain = Encoding.UTF8.GetBytes(pin.Reveal());
        try
        {
            var wrapped = Seal(_kek, dataKey);
            var sealedPin = Seal(dataKey, plain);
            var output = new byte[4 + wrapped.Length + sealedPin.Length];
            BinaryPrimitives.WriteInt32BigEndian(output, wrapped.Length);
            wrapped.CopyTo(output, 4);
            sealedPin.CopyTo(output, 4 + wrapped.Length);
            return Task.FromResult((output, LocalKeyId));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public Task<Pin> DecryptAsync(byte[] ciphertext, string keyId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        if (keyId != LocalKeyId)
        {
            throw new CryptographicException("Unknown PIN key id.");
        }

        if (ciphertext.Length < 4)
        {
            throw new CryptographicException("PIN ciphertext is too short.");
        }

        var wrappedLength = BinaryPrimitives.ReadInt32BigEndian(ciphertext);
        if (wrappedLength != NonceSize + TagSize + KeySize || ciphertext.Length < 4 + wrappedLength + NonceSize + TagSize)
        {
            throw new CryptographicException("PIN ciphertext is malformed.");
        }

        var dataKey = Open(_kek, ciphertext.AsSpan(4, wrappedLength));
        try
        {
            var plain = Open(dataKey, ciphertext.AsSpan(4 + wrappedLength));
            try
            {
                var pin = Pin.Create(Encoding.UTF8.GetString(plain));
                return pin.IsSuccess ? Task.FromResult(pin.Value) : throw new CryptographicException("Decrypted PIN is invalid.");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    public string ProtectForTransport(byte[] ciphertext, string keyId)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        return IPinVault.TransportPrefix + Convert.ToBase64String(ciphertext) + ":" + keyId;
    }

    public (byte[] Ciphertext, string KeyId)? UnprotectTransport(string value)
    {
        if (string.IsNullOrEmpty(value) || !value.StartsWith(IPinVault.TransportPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = value[IPinVault.TransportPrefix.Length..];
        var separator = rest.LastIndexOf(':');
        if (separator <= 0)
        {
            return null;
        }

        try
        {
            return (Convert.FromBase64String(rest[..separator]), rest[(separator + 1)..]);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static byte[] Seal(byte[] key, byte[] plain)
    {
        var output = new byte[NonceSize + TagSize + plain.Length];
        var nonce = output.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plain, output.AsSpan(NonceSize + TagSize), output.AsSpan(NonceSize, TagSize));
        return output;
    }

    private static byte[] Open(byte[] key, ReadOnlySpan<byte> sealedValue)
    {
        var plain = new byte[sealedValue.Length - NonceSize - TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(sealedValue[..NonceSize], sealedValue[(NonceSize + TagSize)..], sealedValue.Slice(NonceSize, TagSize), plain);
        return plain;
    }
}
