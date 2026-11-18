using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Gatehouse.IntegrationTests.Scenarios.Infrastructure;

/// <summary>
/// RFC 6238 TOTP (HMAC-SHA1, 6 digits, 30 s) like tests/e2e/fixtures/totp.ts: Keycloak stores the OTP secret as a raw string, its
/// HMAC key is the UTF-8 bytes of that string.
/// </summary>
public static class Totp
{
    private const int StepSeconds = 30;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static long _lastCounter = -1;

    public static string Code(string rawSecret, DateTimeOffset at)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, at.ToUnixTimeSeconds() / StepSeconds);
#pragma warning disable CA5350 // RFC 6238 TOTP is defined over HMAC-SHA1 (Keycloak's default OTP algorithm).
        var hmac = HMACSHA1.HashData(Encoding.UTF8.GetBytes(rawSecret), counter);
#pragma warning restore CA5350
        var offset = hmac[^1] & 0x0f;
        var binary = ((hmac[offset] & 0x7f) << 24) | (hmac[offset + 1] << 16) | (hmac[offset + 2] << 8) | hmac[offset + 3];
        return (binary % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    /// <summary>A code of a step not used before by this process (Keycloak refuses a replay), with at least 5 s left in the step.</summary>
    public static async Task<string> FreshAsync(string rawSecret, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            while (true)
            {
                var now = DateTimeOffset.UtcNow;
                var seconds = now.ToUnixTimeMilliseconds() / 1000d;
                var counter = (long)(seconds / StepSeconds);
                var left = StepSeconds - (seconds % StepSeconds);
                if (counter > _lastCounter && left >= 5)
                {
                    _lastCounter = counter;
                    return Code(rawSecret, now);
                }

                await Task.Delay(TimeSpan.FromSeconds(left + 0.5), ct);
            }
        }
        finally
        {
            Gate.Release();
        }
    }
}
