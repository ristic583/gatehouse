namespace Gatehouse.Application.LiveBuilding.ListAlarms;

/// <summary><c>Status</c>: <c>open</c> (default) or <c>acknowledged</c>.</summary>
public sealed record ListAlarmsQuery(string? Status, Guid? SiteId);
