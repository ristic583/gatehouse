namespace CloudPacs.Simulator;

public sealed record SwipeRequest(string DoorRef, string? CardNumber, string? ExternalRef, string? Direction = null);
