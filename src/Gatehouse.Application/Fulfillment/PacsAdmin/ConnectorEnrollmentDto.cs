using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.Application.Fulfillment.PacsAdmin;

/// <summary>
/// Shown once (<c>Cache-Control: no-store</c>); the token is never logged or stored in clear. <see cref="AgentImage"/> comes from
/// <c>Gateway:AgentImage</c>; null means no published image, so the install snippet shows build instructions instead.
/// </summary>
public sealed record ConnectorEnrollmentDto(
    Guid ConnectorId,
    [property: Sensitive] string EnrollmentToken,
    DateTimeOffset ExpiresAt,
    string GatewayUrl,
    string CaCertificatePem,
    PacsVendor Vendor,
    string? AgentImage = null);
