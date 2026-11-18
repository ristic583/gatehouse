namespace Gatehouse.Api.Features.LiveBuilding;

/// <summary>One hub connection's membership of a site group, with the identity it joined as.</summary>
public sealed record LiveSubscription(string ConnectionId, string Tenant, string Subject, string? Email, Guid SiteId);
