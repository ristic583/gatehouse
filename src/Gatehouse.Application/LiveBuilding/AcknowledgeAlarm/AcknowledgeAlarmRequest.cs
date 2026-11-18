namespace Gatehouse.Application.LiveBuilding.AcknowledgeAlarm;

/// <summary>Body of <c>POST /api/v1/live/alarms/{alarmId}/acknowledge</c>.</summary>
public sealed record AcknowledgeAlarmRequest(string? Comment);
