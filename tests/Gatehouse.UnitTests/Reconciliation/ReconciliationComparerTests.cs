using Gatehouse.Domain.Access;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Reconciliation;
using Gatehouse.Domain.Sites;

namespace Gatehouse.UnitTests.Reconciliation;

public sealed class ReconciliationComparerTests
{
    private const string Level = "L-1";
    private const string Card = "4111222233334444";
    private static readonly DateTimeOffset Captured = new(2026, 11, 10, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Start = Captured.AddDays(-10);
    private static readonly DateTimeOffset End = Captured.AddDays(20);
    private static readonly AreaId Area = AreaId.New();

    private static readonly PersonId Person = PersonId.New();

    private static string Ref(PersonId id) => id.Value.ToString();

    private static GrantState Grant(
        PersonId? person = null,
        FulfillmentStatus status = FulfillmentStatus.Confirmed,
        EntitlementStatus entitlement = EntitlementStatus.Approved,
        DateTimeOffset? end = null) =>
        new(FulfillmentId.New(), EntitlementId.New(), person ?? Person, Area, Level, status, entitlement, Start, end ?? End);

    private static ExportedAssignment Row(
        PersonId? person = null,
        string? card = Card,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        bool active = true,
        string pacsRef = "A-1") =>
        new(pacsRef, Ref(person ?? Person), card, Level, from ?? Start, to ?? End, active);

    private static ReconciliationOutcome Compare(
        IReadOnlyList<GrantState> grants,
        IReadOnlyList<ExportedAssignment> rows,
        bool nativeValidity = false,
        IReadOnlyList<InFlightKey>? inFlight = null) =>
        ReconciliationComparer.Compare(new ReconciliationInput(
            Captured,
            nativeValidity,
            grants,
            inFlight ?? [],
            new Dictionary<string, IReadOnlySet<string>> { [Ref(Person)] = new HashSet<string> { Card } },
            new Dictionary<string, AreaId> { [Level] = Area },
            rows));

    [Fact]
    public void Current_and_renewal_expect_one_merged_window()
    {
        var current = Grant(end: Captured.AddDays(5));
        var renewal = Grant() with { RequestedStart = Captured.AddDays(5), EffectiveEnd = Captured.AddDays(60) };

        var outcome = Compare([current, renewal], [Row(from: Start, to: Captured.AddDays(60))]);

        Assert.Empty(outcome.Findings);
        Assert.Equal(1, outcome.InSyncCount);
    }

    [Fact]
    public void Confirmed_grant_present_is_in_sync()
    {
        var outcome = Compare([Grant()], [Row()]);
        Assert.Empty(outcome.Findings);
        Assert.Equal(1, outcome.InSyncCount);
        Assert.Equal(1, outcome.ComparedRecordCount);
    }

    [Fact]
    public void Confirmed_grant_absent_is_missing_in_pacs()
    {
        var grant = Grant();
        var finding = Assert.Single(Compare([grant], []).Findings);
        Assert.Equal(FindingKind.MissingInPacs, finding.Kind);
        Assert.Equal(grant.FulfillmentId, finding.FulfillmentId);
        Assert.Equal(grant.EntitlementId, finding.EntitlementId);
        Assert.Equal(Person, finding.PersonId);
        Assert.Equal(Area, finding.AreaId);
        Assert.Null(finding.PacsRef);
        Assert.Equal(End, finding.Details.Expected!.ValidTo);
    }

    [Fact]
    public void Active_row_without_entitlement_is_extra_in_pacs()
    {
        var outcome = Compare([], [Row()]);
        var finding = Assert.Single(outcome.Findings);
        Assert.Equal(FindingKind.ExtraInPacs, finding.Kind);
        Assert.Equal(Person, finding.PersonId);
        Assert.Equal(Area, finding.AreaId);
        Assert.Equal("A-1", finding.PacsRef);
        Assert.Equal(Card, finding.PacsCardNumber);
        Assert.Null(finding.EntitlementId);

        var stranger = Assert.Single(Compare([], [Row(person: PersonId.New())]).Findings);
        Assert.Null(stranger.PersonId);
    }

    [Fact]
    public void Revoked_and_confirmed_row_still_active_is_extra_in_pacs()
    {
        var finding = Assert.Single(Compare([Grant(entitlement: EntitlementStatus.Revoked)], [Row()]).Findings);
        Assert.Equal(FindingKind.ExtraInPacs, finding.Kind);
        Assert.Null(finding.FulfillmentId);
    }

    [Fact]
    public void Different_card_is_mismatch()
    {
        var outcome = Compare([Grant()], [Row(card: "5555666677778888")]);
        var finding = Assert.Single(outcome.Findings);
        Assert.Equal(FindingKind.Mismatch, finding.Kind);
        Assert.Equal(["cardNumber"], finding.Details.Differences);
        Assert.Equal("5555666677778888", finding.PacsCardNumber);
        Assert.Equal(0, outcome.InSyncCount);
        Assert.Equal(1, outcome.ComparedRecordCount);
    }

    [Fact]
    public void Different_end_date_is_mismatch_only_with_native_validity()
    {
        var rows = new[] { Row(to: End.AddDays(5)) };
        Assert.Empty(Compare([Grant()], rows, nativeValidity: false).Findings);

        var finding = Assert.Single(Compare([Grant()], rows, nativeValidity: true).Findings);
        Assert.Equal(FindingKind.Mismatch, finding.Kind);
        Assert.Equal(["validTo"], finding.Details.Differences);

        Assert.Empty(Compare([Grant()], [Row(to: End.AddMilliseconds(900))], nativeValidity: true).Findings);
    }

    [Fact]
    public void Expired_entitlement_with_active_row_is_expired_still_present()
    {
        var expired = Grant(end: Captured.AddDays(-1));
        var finding = Assert.Single(Compare([expired], [Row(to: null)]).Findings);
        Assert.Equal(FindingKind.ExpiredStillPresent, finding.Kind);
        Assert.Equal(expired.EntitlementId, finding.EntitlementId);
        Assert.Equal(expired.FulfillmentId, finding.FulfillmentId);

        var markedExpired = Grant(entitlement: EntitlementStatus.Expired, end: Captured.AddDays(-1));
        Assert.Equal(FindingKind.ExpiredStillPresent, Assert.Single(Compare([markedExpired], [Row(to: null)]).Findings).Kind);
    }

    [Fact]
    public void Inactive_rows_are_ignored_not_findings()
    {
        var outcome = Compare([], [Row(active: false), Row(active: false, pacsRef: "A-2")]);
        Assert.Empty(outcome.Findings);
        Assert.Equal(2, outcome.IgnoredInactiveCount);
        Assert.Equal(0, outcome.ComparedRecordCount);
    }

    [Fact]
    public void Rows_with_past_valid_to_are_history()
    {
        var outcome = Compare([Grant()], [Row(to: Captured.AddSeconds(-1))]);
        Assert.Equal(1, outcome.IgnoredInactiveCount);
        Assert.Equal(FindingKind.MissingInPacs, Assert.Single(outcome.Findings).Kind);
    }

    [Fact]
    public void In_flight_keys_are_skipped_not_compared()
    {
        var pending = PersonId.New();
        var revoking = PersonId.New();
        var outcome = Compare(
            [Grant(person: pending, status: FulfillmentStatus.Pending)],
            [Row(person: pending), Row(person: revoking, pacsRef: "A-2")],
            inFlight: [new InFlightKey(Ref(revoking), Level)]);
        Assert.Empty(outcome.Findings);
        Assert.Equal(2, outcome.SkippedInFlightCount);
        Assert.Equal(0, outcome.ComparedRecordCount);
    }

    [Fact]
    public void Compared_count_covers_only_compared_records()
    {
        var other = PersonId.New();
        var outcome = Compare(
            [Grant(), Grant(person: other)],
            [Row(), Row(pacsRef: "A-0"), Row(person: PersonId.New(), pacsRef: "A-3"), Row(active: false, pacsRef: "A-4")]);
        // expected keys: 2 (Person in sync, other missing); extra duplicate row of Person; one unknown person's row.
        Assert.Equal(4, outcome.ComparedRecordCount);
        Assert.Equal(1, outcome.InSyncCount);
        Assert.Equal(1, outcome.IgnoredInactiveCount);
        Assert.Equal(
            [FindingKind.MissingInPacs, FindingKind.ExtraInPacs, FindingKind.ExtraInPacs],
            outcome.Findings.Select(f => f.Kind).OrderBy(k => k).ToArray());
        Assert.Equal(outcome.Findings.OrderBy(f => f.Kind).ToList(), outcome.Findings);
    }

    [Fact]
    public void Findings_never_contain_full_card_numbers_in_details()
    {
        var outcome = Compare(
            [Grant(), Grant(person: PersonId.New(), end: Captured.AddDays(-1))],
            [Row(card: "5555666677778888", to: End.AddDays(1)), Row(person: PersonId.New(), pacsRef: "A-9")],
            nativeValidity: true);
        Assert.NotEmpty(outcome.Findings);
        foreach (var finding in outcome.Findings)
        {
            var details = System.Text.Json.JsonSerializer.Serialize(finding.Details);
            Assert.DoesNotContain(Card, details, StringComparison.Ordinal);
            Assert.DoesNotContain("5555666677778888", details, StringComparison.Ordinal);
        }

        Assert.Contains(outcome.Findings, f => f.Details.Actual?.CardNumberMasked is { Length: > 0 });
    }
}
