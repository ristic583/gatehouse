namespace CloudPacs.Simulator;

/// <summary>Demo data: nordlys Warehouse Aarhus (site AAR-WH).</summary>
public static class CloudPacsSeed
{
    public static IReadOnlyList<SimAccessGroup> Groups { get; } =
    [
        new("AG-AAR-DOCK", "Loading Dock"),
        new("AG-AAR-OFFICE", "Warehouse Office"),
    ];

    public static IReadOnlyList<SimDoor> Doors { get; } =
    [
        new("D-AAR-DOCK", "AG-AAR-DOCK"),
        new("D-AAR-OFFICE", "AG-AAR-OFFICE"),
    ];
}
