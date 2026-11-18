using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Gatehouse.Infrastructure.Security;

/// <summary>Purpose-bound 256-bit keys derived from <c>Gatehouse:Secrets:MasterKey</c> with HKDF-SHA256.</summary>
public static class MasterKeyDerivation
{
    public static byte[] Derive(IConfiguration configuration, string info)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var masterKey = configuration[AesGcmSecretProtector.MasterKeySetting];
        if (string.IsNullOrEmpty(masterKey) || masterKey.Length < AesGcmSecretProtector.MinimumMasterKeyLength)
        {
            throw new InvalidOperationException(
                $"Configuration '{AesGcmSecretProtector.MasterKeySetting}' is missing or shorter than "
                + $"{AesGcmSecretProtector.MinimumMasterKeyLength} characters.");
        }

        return HKDF.DeriveKey(
            HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(masterKey), 32, salt: [], info: Encoding.UTF8.GetBytes(info));
    }
}
