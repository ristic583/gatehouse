namespace Gatehouse.IntegrationTests.Scenarios.Infrastructure;

/// <summary>A row of LegacyPACS <c>GET /assignments</c>.</summary>
public sealed record LegacyAssignment(string PersonRef, string? CardNumber, string AccessLevelRef, DateTimeOffset? ValidFrom, DateTimeOffset? ValidTo, bool IsActive);
