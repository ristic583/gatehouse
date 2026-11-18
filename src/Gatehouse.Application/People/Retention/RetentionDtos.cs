namespace Gatehouse.Application.People.Retention;

public sealed record RetentionSettingsDto(int PhotoDaysAfterTermination, int PersonalDataDaysAfterTermination, int DoorEventDays);

public sealed record SetRetentionSettingRequest(string? Key, int Days);

public sealed record RetentionRunResult(int PhotosDeleted, int PeopleAnonymized);
