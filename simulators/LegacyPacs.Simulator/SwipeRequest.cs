namespace LegacyPacs.Simulator;

public sealed record SwipeRequest(string DoorRef, string? CardNumber = null, string? PersonRef = null, string? HolderName = null, string? Direction = null);
