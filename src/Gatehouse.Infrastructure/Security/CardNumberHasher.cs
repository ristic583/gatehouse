using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Gatehouse.Application.Connectors;
using Gatehouse.Domain.Common;
using Microsoft.Extensions.Configuration;

namespace Gatehouse.Infrastructure.Security;

/// <summary>
/// Per-tenant HMAC key = HKDF(<c>Gatehouse:Secrets:MasterKey</c>, "gatehouse-card-hash-v1") expanded with the tenant id (PT-34).
/// Nothing is stored: the key is re-derived on start. Rotating the master key changes every card hash (presence keys reset).
/// </summary>
public sealed class CardNumberHasher(IConfiguration configuration) : ICardNumberHasher
{
    private readonly byte[] _baseKey = MasterKeyDerivation.Derive(configuration, "gatehouse-card-hash-v1");
    private readonly ConcurrentDictionary<string, byte[]> _tenantKeys = new(StringComparer.Ordinal);

    public string? Hash(TenantId tenantId, string? cardNumber)
    {
        if (string.IsNullOrEmpty(cardNumber))
        {
            return null;
        }

        var key = _tenantKeys.GetOrAdd(tenantId.Value, static (tenant, baseKey) =>
            HKDF.Expand(HashAlgorithmName.SHA256, baseKey, 32, Encoding.UTF8.GetBytes("tenant:" + tenant)), _baseKey);
        return Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(cardNumber)));
    }
}
