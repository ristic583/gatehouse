namespace Gatehouse.IntegrationTests.Scenarios.Infrastructure;

/// <summary>Areas from the Nordlys demo seed (NordlysSeed): one per PACS, both security level Medium (area owner approves).</summary>
public static class ScenarioAreas
{
    /// <summary>HQ Lab on LegacyPACS (owner Sofie Berg).</summary>
    public static ScenarioArea HqLab { get; } = new("CPH-HQ", "CPH-LAB", "D-CPH-LAB", "AL-CPH-LAB", "HQ-Copenhagen");

    /// <summary>Aarhus Loading Dock on CloudPACS (owner Erik Møller).</summary>
    public static ScenarioArea AarhusDock { get; } = new("AAR-WH", "AAR-DOCK", "D-AAR-DOCK", "AG-AAR-DOCK", "Warehouse-Aarhus");
}
