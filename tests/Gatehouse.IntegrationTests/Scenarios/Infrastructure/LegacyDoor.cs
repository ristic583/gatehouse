namespace Gatehouse.IntegrationTests.Scenarios.Infrastructure;

/// <summary>A row of LegacyPACS <c>GET /doors</c>.</summary>
public sealed record LegacyDoor(string DoorRef, string AccessLevelRef, bool Lockdown, DateTimeOffset? UnlockedUntil);
