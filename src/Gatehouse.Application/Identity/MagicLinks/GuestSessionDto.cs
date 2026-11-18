using Gatehouse.Domain.Common;

namespace Gatehouse.Application.Identity.MagicLinks;

public sealed record GuestSessionDto(
    [property: Sensitive] string AccessToken, DateTimeOffset ExpiresAt, string Sub, string Email, string Name, string TenantId);
