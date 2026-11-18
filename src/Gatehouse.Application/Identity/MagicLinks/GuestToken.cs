using Gatehouse.Domain.Common;

namespace Gatehouse.Application.Identity.MagicLinks;

public sealed record GuestToken([property: Sensitive] string AccessToken, DateTimeOffset ExpiresAt, string Subject);
