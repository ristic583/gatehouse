using Gatehouse.Domain.Common;

namespace Gatehouse.Application.Connectors;

/// <summary>
/// Gateway side of door events (connector-protocol.md §2.4): card numbers are replaced by a keyed hash before anything is published
/// or stored. HMAC-SHA-256 with a per-tenant key that never leaves the server (PT-34), so the 6-10 digit number space cannot be
/// enumerated without the key, and the same card hashes differently in two tenants.
/// </summary>
public interface ICardNumberHasher
{
    /// <summary>Lowercase hex HMAC-SHA-256, or null for a missing card number.</summary>
    public string? Hash(TenantId tenantId, string? cardNumber);
}
