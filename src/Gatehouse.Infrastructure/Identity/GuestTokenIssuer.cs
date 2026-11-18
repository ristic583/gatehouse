using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gatehouse.Application.Identity.MagicLinks;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;
using Gatehouse.Infrastructure.Security;
using Microsoft.Extensions.Configuration;

namespace Gatehouse.Infrastructure.Identity;

/// <summary>
/// HS256 guest JWT (key: HKDF of the master key, info <c>gatehouse-guest-jwt-v1</c>): iss <c>gatehouse-guest</c>, aud <c>gatehouse-api</c>,
/// sub <c>guest:{personId}</c>, email, given_name, family_name, tenant_id, 30-minute exp. The Api's <c>Guest</c> scheme validates it with
/// <see cref="SigningKey"/>.
/// </summary>
public sealed class GuestTokenIssuer(IConfiguration configuration) : IGuestTokenIssuer
{
    public const string Issuer = "gatehouse-guest";
    public const string Audience = "gatehouse-api";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private static readonly byte[] Header = Encoding.UTF8.GetBytes("""{"alg":"HS256","typ":"JWT"}""");

    public byte[] SigningKey { get; } = MasterKeyDerivation.Derive(configuration, "gatehouse-guest-jwt-v1");

    public GuestToken Issue(Person guest, TenantId tenantId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(guest);
        var expiresAt = now + Lifetime;
        var subject = $"guest:{guest.Id.Value}";
        var payload = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["iss"] = Issuer,
            ["aud"] = Audience,
            ["sub"] = subject,
            ["email"] = guest.Email,
            ["given_name"] = guest.GivenName,
            ["family_name"] = guest.FamilyName,
            ["tenant_id"] = tenantId.Value,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["nbf"] = now.ToUnixTimeSeconds(),
            ["exp"] = expiresAt.ToUnixTimeSeconds(),
            ["jti"] = Guid.CreateVersion7().ToString("N"),
        });
        var signingInput = Base64Url.EncodeToString(Header) + "." + Base64Url.EncodeToString(payload);
        var signature = HMACSHA256.HashData(SigningKey, Encoding.ASCII.GetBytes(signingInput));
        return new GuestToken(signingInput + "." + Base64Url.EncodeToString(signature), expiresAt, subject);
    }
}
