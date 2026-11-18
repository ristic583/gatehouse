using Gatehouse.Domain.Common;

namespace Gatehouse.Application.Identity.MagicLinks;

/// <summary>Returned once by <c>POST /api/v1/people/{id}/magic-links</c>; the URL carries the only copy of the token.</summary>
public sealed record IssuedMagicLinkDto([property: Sensitive] string Url, DateTimeOffset ExpiresAt);
