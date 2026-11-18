using Gatehouse.Domain.Common;

namespace Gatehouse.Application.Fulfillment.PacsAdmin;

/// <summary>A new registration and its one-time enrollment token (never stored in clear, never logged).</summary>
public sealed record ConnectorTicket(Guid ConnectorId, [property: Sensitive] string EnrollmentToken, DateTimeOffset ExpiresAt);
