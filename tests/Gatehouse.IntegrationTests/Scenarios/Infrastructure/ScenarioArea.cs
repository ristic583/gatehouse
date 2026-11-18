namespace Gatehouse.IntegrationTests.Scenarios.Infrastructure;

/// <summary>A demo area with its door and the vendor access level the seed maps it to.</summary>
public sealed record ScenarioArea(string SiteCode, string Code, string DoorRef, string AccessLevelRef, string PacsInstanceName);
