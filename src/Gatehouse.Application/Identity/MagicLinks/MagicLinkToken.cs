using Gatehouse.Domain.Common;
using Gatehouse.Domain.Identity;

namespace Gatehouse.Application.Identity.MagicLinks;

/// <summary>A decoded, signature-verified magic link token.</summary>
public sealed record MagicLinkToken(TenantId TenantId, MagicLinkId LinkId, byte[] Secret);
