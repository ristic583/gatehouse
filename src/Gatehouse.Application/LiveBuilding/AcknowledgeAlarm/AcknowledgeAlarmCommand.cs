namespace Gatehouse.Application.LiveBuilding.AcknowledgeAlarm;

public sealed record AcknowledgeAlarmCommand(Guid AlarmId, string? Comment);
