namespace LegacyPacs.Simulator;

public sealed record DoorRow(string DoorRef, string AccessLevelRef, bool Lockdown, DateTimeOffset? UnlockedUntil);
