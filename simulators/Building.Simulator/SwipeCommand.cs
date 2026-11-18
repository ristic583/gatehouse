namespace Building.Simulator;

public sealed record SwipeCommand(string DoorRef, string? CardNumber = null, string? PersonRef = null, string? HolderName = null, string? Direction = null);
