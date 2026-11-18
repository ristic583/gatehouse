namespace Gatehouse.Application.People.Lifecycle;

public sealed record LifecycleSettingsDto(
    int MaxSponsorshipDays, int MaxVisitorDays, int RenewalEmployeeDays, int RenewalExternalDays, int RenewalGuestDays,
    int RenewalLeadDays);
