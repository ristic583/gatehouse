using Gatehouse.Domain.Common;

namespace Gatehouse.Application.Identity.MagicLinks;

public sealed record GuestSessionRequest([property: Sensitive] string? Token);
