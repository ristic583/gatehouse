using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Gatehouse.Application.Identity.MagicLinks;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Identity;
using Gatehouse.Infrastructure.Security;
using Microsoft.Extensions.Configuration;

namespace Gatehouse.Infrastructure.Identity;

/// <summary>
/// <c>base64url(tenantId|linkId|base64url(secret)) + "." + base64url(HMAC-SHA256)</c> with a key derived from the master key
/// (HKDF-SHA256, info <c>gatehouse-magic-link-v1</c>). The HMAC is checked in constant time before anything is parsed.
/// </summary>
public sealed class MagicLinkTokenCodec : IMagicLinkTokenCodec
{
    private const int MaxTokenLength = 512;
    private readonly byte[] _key;

    public MagicLinkTokenCodec(IConfiguration configuration) =>
        _key = MasterKeyDerivation.Derive(configuration, "gatehouse-magic-link-v1");

    public string Encode(TenantId tenantId, MagicLinkId linkId, byte[] secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        var payload = Encoding.UTF8.GetBytes($"{tenantId.Value}|{linkId.Value:N}|{Base64Url.EncodeToString(secret)}");
        return Base64Url.EncodeToString(payload) + "." + Base64Url.EncodeToString(HMACSHA256.HashData(_key, payload));
    }

    public MagicLinkToken? TryDecode(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > MaxTokenLength)
        {
            return null;
        }

        var dot = token.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0 || dot == token.Length - 1)
        {
            return null;
        }

        byte[] payload;
        byte[] signature;
        try
        {
            payload = Base64Url.DecodeFromChars(token.AsSpan(0, dot));
            signature = Base64Url.DecodeFromChars(token.AsSpan(dot + 1));
        }
        catch (FormatException)
        {
            return null;
        }

        if (!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(_key, payload), signature))
        {
            return null;
        }

        var parts = Encoding.UTF8.GetString(payload).Split('|');
        if (parts.Length != 3 || !Guid.TryParseExact(parts[1], "N", out var linkId))
        {
            return null;
        }

        var tenant = TenantId.Create(parts[0]);
        if (tenant.IsFailure)
        {
            return null;
        }

        try
        {
            return new MagicLinkToken(tenant.Value, new MagicLinkId(linkId), Base64Url.DecodeFromChars(parts[2]));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
