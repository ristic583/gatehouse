namespace Gatehouse.Domain.People;

/// <summary>GDPR retention per tenant (<c>tenant_settings</c> keys <c>retention.*</c>, days). Audit has no retention: the chain is
/// kept.</summary>
public sealed record RetentionSettings(int PhotoDaysAfterTermination, int PersonalDataDaysAfterTermination, int DoorEventDays)
{
    public const string PhotoDaysKey = "retention.photo_days_after_termination";
    public const string PersonalDataDaysKey = "retention.personal_data_days_after_termination";
    public const string DoorEventDaysKey = "retention.door_event_days";

    public static readonly RetentionSettings Defaults = new(30, 365, 90);

    /// <summary>Allowed range per key.</summary>
    public static readonly IReadOnlyDictionary<string, (int Min, int Max)> Ranges = new Dictionary<string, (int, int)>(
        StringComparer.Ordinal)
    {
        [PhotoDaysKey] = (0, 3650),
        [PersonalDataDaysKey] = (30, 3650),
        [DoorEventDaysKey] = (7, 365),
    };

    public RetentionSettings With(string key, int days) => key switch
    {
        PhotoDaysKey => this with { PhotoDaysAfterTermination = days },
        PersonalDataDaysKey => this with { PersonalDataDaysAfterTermination = days },
        DoorEventDaysKey => this with { DoorEventDays = days },
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown retention setting."),
    };
}
