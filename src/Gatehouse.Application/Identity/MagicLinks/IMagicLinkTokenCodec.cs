using Gatehouse.Domain.Common;
using Gatehouse.Domain.Identity;

namespace Gatehouse.Application.Identity.MagicLinks;

/// <summary>Signs and verifies magic link tokens (HMAC-SHA256); the tenant inside a verified token is a platform-signed
/// credential.</summary>
public interface IMagicLinkTokenCodec
{
    public string Encode(TenantId tenantId, MagicLinkId linkId, byte[] secret);

    /// <summary>Verifies the HMAC in constant time before parsing; null for any malformed or tampered token.</summary>
    public MagicLinkToken? TryDecode(string? token);
}
