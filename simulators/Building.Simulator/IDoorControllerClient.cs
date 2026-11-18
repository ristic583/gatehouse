namespace Building.Simulator;

/// <summary>Controller-side endpoints of a PACS simulator. Door event types use the LegacyPACS names (DoorOpened, DoorClosed, ...).</summary>
public interface IDoorControllerClient
{
    /// <summary>Returns null when the controller does not know the door.</summary>
    public Task<ControllerDecision?> SwipeAsync(SwipeCommand command, CancellationToken ct);

    public Task DoorEventAsync(string doorRef, string type, CancellationToken ct);

    public Task<IReadOnlyList<ControllerDoor>> ListDoorsAsync(CancellationToken ct);

    public Task<IReadOnlyList<ControllerAssignment>> ListAssignmentsAsync(CancellationToken ct);
}
