namespace Gatehouse.IntegrationTests.Scenarios.Infrastructure;

/// <summary>A fulfillment of an entitlement as stored in the tenant database.</summary>
public sealed record FulfillmentRow(Guid FulfillmentId, string Kind, string Status, Guid PacsInstanceId, string? PacsRef);
