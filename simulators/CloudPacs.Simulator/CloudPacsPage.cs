namespace CloudPacs.Simulator;

public sealed record CloudPacsPage<T>(IReadOnlyList<T> Items, string? NextCursor);
