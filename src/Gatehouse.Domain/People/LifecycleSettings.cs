namespace Gatehouse.Domain.People;

/// <summary>Per-tenant lifecycle caps and renewal intervals in days (spec §5.6; stored as <c>tenant_settings</c> keys).</summary>
public sealed record LifecycleSettings(
    int MaxSponsorshipDays,
    int MaxVisitorDays,
    int RenewalEmployeeDays,
    int RenewalExternalDays,
    int RenewalGuestDays,
    int RenewalLeadDays)
{
    public const string MaxSponsorshipDaysKey = "lifecycle.max_sponsorship_days";
    public const string MaxVisitorDaysKey = "lifecycle.max_visitor_days";
    public const string RenewalEmployeeDaysKey = "lifecycle.renewal_employee_days";
    public const string RenewalExternalDaysKey = "lifecycle.renewal_external_days";
    public const string RenewalGuestDaysKey = "lifecycle.renewal_guest_days";
    public const string RenewalLeadDaysKey = "lifecycle.renewal_lead_days";

    public static LifecycleSettings Defaults { get; } = new(365, 7, 1825, 365, 30, 14);

    public static IReadOnlyList<string> Keys { get; } =
    [
        MaxSponsorshipDaysKey, MaxVisitorDaysKey, RenewalEmployeeDaysKey, RenewalExternalDaysKey, RenewalGuestDaysKey, RenewalLeadDaysKey,
    ];

    /// <summary>Longest allowed eligibility window for a local person of this type; <c>null</c> for employees.</summary>
    public int? MaxDays(PersonType type) => type switch
    {
        PersonType.External or PersonType.Guest => MaxSponsorshipDays,
        PersonType.Visitor => MaxVisitorDays,
        _ => null,
    };

    public int RenewalDays(PersonType type) => type switch
    {
        PersonType.Employee => RenewalEmployeeDays,
        PersonType.External => RenewalExternalDays,
        _ => RenewalGuestDays,
    };

    public int Get(string key) => key switch
    {
        MaxSponsorshipDaysKey => MaxSponsorshipDays,
        MaxVisitorDaysKey => MaxVisitorDays,
        RenewalEmployeeDaysKey => RenewalEmployeeDays,
        RenewalExternalDaysKey => RenewalExternalDays,
        RenewalGuestDaysKey => RenewalGuestDays,
        RenewalLeadDaysKey => RenewalLeadDays,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown lifecycle setting."),
    };

    public LifecycleSettings With(string key, int days) => key switch
    {
        MaxSponsorshipDaysKey => this with { MaxSponsorshipDays = days },
        MaxVisitorDaysKey => this with { MaxVisitorDays = days },
        RenewalEmployeeDaysKey => this with { RenewalEmployeeDays = days },
        RenewalExternalDaysKey => this with { RenewalExternalDays = days },
        RenewalGuestDaysKey => this with { RenewalGuestDays = days },
        RenewalLeadDaysKey => this with { RenewalLeadDays = days },
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown lifecycle setting."),
    };
}
