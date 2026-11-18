using Gatehouse.Domain.Common;
using Gatehouse.Domain.People.Events;

namespace Gatehouse.Domain.People;

public sealed class Person : AggregateRoot<PersonId>
{
    private Person(
        PersonId id,
        PersonSource source,
        PersonType type,
        string givenName,
        string familyName,
        string email,
        Eligibility eligibility,
        PersonId? managerId,
        PersonId? sponsorId)
        : base(id)
    {
        Source = source;
        Type = type;
        GivenName = givenName;
        FamilyName = familyName;
        Email = email;
        Eligibility = eligibility;
        ManagerId = managerId;
        SponsorId = sponsorId;
        Status = PersonStatus.Active;
    }

    // EF Core
    private Person()
        : base(default)
    {
        GivenName = string.Empty;
        FamilyName = string.Empty;
        Email = string.Empty;
        Eligibility = null!;
    }

    public PersonSource Source { get; private set; }

    public PersonType Type { get; private set; }

    public string GivenName { get; private set; }

    public string FamilyName { get; private set; }

    public string DisplayName => GivenName + " " + FamilyName;

    public string Email { get; private set; }

    public string? IdentitySubject { get; private set; }

    public PersonId? ManagerId { get; private set; }

    public PersonId? SponsorId { get; private set; }

    public PersonStatus Status { get; private set; }

    public Eligibility Eligibility { get; private set; }

    public uint Version { get; private set; }

    /// <summary>When the person was terminated (M9; backfilled from the eligibility end for older rows).</summary>
    public DateTimeOffset? TerminatedAt { get; private set; }

    /// <summary>When retention anonymized the person (GDPR, M9); names, email and identity link are gone after this.</summary>
    public DateTimeOffset? AnonymizedAt { get; private set; }

    public static Result<Person> Create(
        PersonSource source,
        PersonType type,
        string givenName,
        string familyName,
        string email,
        Eligibility eligibility,
        PersonId? managerId,
        Person? sponsor,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(eligibility);

        if (type != PersonType.Employee && source != PersonSource.Local)
        {
            return Error.Validation("person.non_employee_must_be_local", "Non-employees must be local people.");
        }

        if (source == PersonSource.Local && sponsor is null)
        {
            return Error.Validation("person.local_requires_sponsor", "A local person needs a sponsor.");
        }

        if (source == PersonSource.Local && eligibility.End is null)
        {
            return Error.Validation("person.local_requires_end_date", "A local person needs an eligibility end date.");
        }

        if (eligibility.End is null && source != PersonSource.Authoritative)
        {
            return Error.Validation("person.invalid_eligibility", "Only authoritative people may have open-ended eligibility.");
        }

        if (sponsor is not null && sponsor.Type != PersonType.Employee)
        {
            return Error.Validation("person.sponsor_not_employee", "A sponsor must be an employee.");
        }

        if (!IsValidName(givenName) || !IsValidName(familyName))
        {
            return Error.Validation("person.invalid_name", "Given and family name must be 1-100 characters.");
        }

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@', StringComparison.Ordinal) || email.Length > 254)
        {
            return Error.Validation("person.invalid_email", "A valid email address is required.");
        }

        var person = new Person(
            PersonId.New(),
            source,
            type,
            givenName.Trim(),
            familyName.Trim(),
            email.Trim().ToLowerInvariant(),
            eligibility,
            managerId,
            sponsor?.Id);

        person.Raise(new PersonCreated(person.Id, type, source, managerId, person.SponsorId, now));
        return person;
    }

    public Result LinkIdentity(string subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        if (IdentitySubject is null)
        {
            IdentitySubject = subject;
            return Result.Success();
        }

        return IdentitySubject == subject
            ? Result.Success()
            : Error.Conflict("person.identity_already_linked", "This person is already linked to another identity.");
    }

    public Result SetManager(PersonId? managerId)
    {
        if (managerId == Id)
        {
            return Error.Validation("person.self_reference", "A person cannot be their own manager.");
        }

        ManagerId = managerId;
        return Result.Success();
    }

    /// <summary>Checks a local person's eligibility window against the tenant caps (spec §5.6).</summary>
    public static Result EnsureWithinCaps(PersonType type, Eligibility eligibility, LifecycleSettings settings)
    {
        ArgumentNullException.ThrowIfNull(eligibility);
        ArgumentNullException.ThrowIfNull(settings);
        var max = settings.MaxDays(type);
        if (max is not null && eligibility.End is not null && (eligibility.End.Value - eligibility.Start).TotalDays > max.Value)
        {
            return Error.Validation(
                "person.sponsorship_too_long", $"The eligibility window exceeds the maximum of {max.Value} days for this person type.");
        }

        return Result.Success();
    }

    /// <summary>Applies an HR feed record; returns whether anything changed.</summary>
    public Result<bool> UpdateFromHr(string givenName, string familyName, PersonId? managerId, Eligibility eligibility, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(eligibility);
        if (Source != PersonSource.Authoritative)
        {
            return Error.Conflict("person.not_local", "Only authoritative people are updated from the HR feed.");
        }

        if (!IsValidName(givenName) || !IsValidName(familyName))
        {
            return Error.Validation("person.invalid_name", "Given and family name must be 1-100 characters.");
        }

        if (managerId == Id)
        {
            return Error.Validation("person.self_reference", "A person cannot be their own manager.");
        }

        var changed = new List<string>();
        if (GivenName != givenName.Trim())
        {
            GivenName = givenName.Trim();
            changed.Add(nameof(GivenName));
        }

        if (FamilyName != familyName.Trim())
        {
            FamilyName = familyName.Trim();
            changed.Add(nameof(FamilyName));
        }

        if (ManagerId != managerId)
        {
            ManagerId = managerId;
            changed.Add(nameof(ManagerId));
        }

        var datesChanged = Eligibility.Start != eligibility.Start || Eligibility.End != eligibility.End;
        if (datesChanged || Eligibility.TimeZoneId != eligibility.TimeZoneId)
        {
            changed.Add(nameof(Eligibility));
        }

        var previousEnd = Eligibility.End;
        Eligibility = eligibility;
        if (changed.Count == 0)
        {
            return false;
        }

        Raise(new PersonUpdated(Id, changed, now));
        if (datesChanged)
        {
            Raise(new EligibilityChanged(Id, eligibility.Start, eligibility.End, previousEnd, now));
        }

        return true;
    }

    /// <summary>Extends or shortens a local person's sponsorship within the tenant caps.</summary>
    public Result ChangeEligibilityEnd(DateTimeOffset end, LifecycleSettings settings, DateTimeOffset now)
    {
        var guard = EnsureLocalAndActive();
        if (guard.IsFailure)
        {
            return guard;
        }

        var eligibility = Eligibility.Create(Eligibility.Start, end, Eligibility.TimeZoneId);
        if (eligibility.IsFailure)
        {
            return eligibility.Error;
        }

        var caps = EnsureWithinCaps(Type, eligibility.Value, settings);
        if (caps.IsFailure)
        {
            return caps;
        }

        var previousEnd = Eligibility.End;
        Eligibility = eligibility.Value;
        Raise(new EligibilityChanged(Id, Eligibility.Start, end, previousEnd, now));
        return Result.Success();
    }

    /// <summary>Ends a local person's sponsorship now; the caller terminates the person.</summary>
    public Result EndSponsorship(PersonId? by, DateTimeOffset now)
    {
        var guard = EnsureLocalAndActive();
        if (guard.IsFailure)
        {
            return guard;
        }

        var previousEnd = Eligibility.End;
        var start = Eligibility.Start < now ? Eligibility.Start : now.AddTicks(-1);
        Eligibility = Eligibility.Create(start, now, Eligibility.TimeZoneId).Value;
        Raise(new SponsorshipEnded(Id, SponsorId!.Value, by, now));
        Raise(new EligibilityChanged(Id, Eligibility.Start, now, previousEnd, now));
        return Result.Success();
    }

    public Result Terminate(DateTimeOffset now)
    {
        if (Status == PersonStatus.Terminated)
        {
            return Error.Conflict("person.terminated", "The person is already terminated.");
        }

        Status = PersonStatus.Terminated;
        TerminatedAt = now;
        Raise(new PersonTerminated(Id, Eligibility.End ?? now, now));
        return Result.Success();
    }

    /// <summary>
    /// GDPR anonymization of a terminated person: names and email are replaced, the identity link is cleared. Ids stay so audit and
    /// history keep their references. Idempotent; refused for people who are not terminated.
    /// </summary>
    public Result Anonymize(DateTimeOffset now)
    {
        if (Status != PersonStatus.Terminated)
        {
            return Error.Conflict("person.not_terminated", "Only terminated people can be anonymized.");
        }

        if (AnonymizedAt is not null)
        {
            return Result.Success();
        }

        GivenName = "Anonymized";
        FamilyName = "Person";
        Email = $"anonymized-{Id.Value:N}@invalid";
        IdentitySubject = null;
        AnonymizedAt = now;
        Raise(new PersonAnonymized(Id, now));
        return Result.Success();
    }

    private Result EnsureLocalAndActive()
    {
        if (Source != PersonSource.Local)
        {
            return Error.Conflict("person.not_local", "Sponsorship changes apply only to local people.");
        }

        return Status == PersonStatus.Terminated
            ? Error.Conflict("person.terminated", "The person is already terminated.")
            : Result.Success();
    }

    private static bool IsValidName(string name) => !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 100;
}
