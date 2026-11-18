namespace Building.Simulator;

public sealed record ControllerAssignment(string PersonRef, string? CardNumber, string AccessLevelRef, bool IsActive);
