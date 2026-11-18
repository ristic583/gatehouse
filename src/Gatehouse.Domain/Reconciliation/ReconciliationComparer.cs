using Gatehouse.Domain.Access;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;

namespace Gatehouse.Domain.Reconciliation;

/// <summary>
/// Compares confirmed Gatehouse state with a PACS export (domain-model.md §2.7 "Comparison"). Pure: no I/O, no clock.
/// <c>SkippedInFlightCount</c> counts in-flight keys that had an expected grant or an active row.
/// </summary>
public static class ReconciliationComparer
{
    private static readonly TimeSpan DateTolerance = TimeSpan.FromSeconds(1);

    public static ReconciliationOutcome Compare(ReconciliationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var capturedAt = input.CapturedAt;

        var inFlight = input.InFlight.Select(k => (k.PersonRef, k.VendorAccessLevelRef)).ToHashSet();
        foreach (var g in input.Grants.Where(g => g.EntitlementStatus == EntitlementStatus.Approved
            && g.Status is FulfillmentStatus.Desired or FulfillmentStatus.Pending or FulfillmentStatus.Failed))
        {
            inFlight.Add(KeyOf(g));
        }

        var settled = input.Grants.Where(g => g.Status is FulfillmentStatus.Confirmed or FulfillmentStatus.Drift).ToList();
        var expected = settled
            .Where(g => g.EntitlementStatus == EntitlementStatus.Approved && g.EffectiveEnd > capturedAt)
            .GroupBy(KeyOf)
            .ToDictionary(grp => grp.Key, Merged);
        var expired = settled
            .Where(g => g.EntitlementStatus is EntitlementStatus.Approved or EntitlementStatus.Expired && g.EffectiveEnd <= capturedAt)
            .GroupBy(KeyOf)
            .ToDictionary(grp => grp.Key, grp => grp.OrderByDescending(g => g.EffectiveEnd).First());
        var knownPersons = input.ActiveCardsByPersonRef.Keys.Concat(input.Grants.Select(g => PersonRefOf(g.PersonId)))
            .ToHashSet(StringComparer.Ordinal);

        var ignoredInactive = 0;
        var active = new List<ExportedAssignment>();
        foreach (var row in input.Rows.Where(r => input.AreaByLevelRef.ContainsKey(r.VendorAccessLevelRef)))
        {
            if (row.IsActive && (row.ValidTo is null || row.ValidTo > capturedAt))
            {
                active.Add(row);
            }
            else
            {
                ignoredInactive++;
            }
        }

        var rowsByKey = active.Where(r => r.PersonRef is not null)
            .GroupBy(r => (r.PersonRef!, r.VendorAccessLevelRef))
            .ToDictionary(grp => grp.Key, grp => grp.OrderBy(r => r.PacsRef, StringComparer.Ordinal).ToList());

        var findings = new List<FindingDraft>();
        var compared = 0;
        var inSync = 0;
        var skippedKeys = new HashSet<(string, string)>();

        foreach (var (key, grant) in expected)
        {
            var rows = rowsByKey.Remove(key, out var found) ? found : [];
            if (inFlight.Contains(key))
            {
                skippedKeys.Add(key);
                continue;
            }

            compared++;
            var cards = input.ActiveCardsByPersonRef.GetValueOrDefault(key.Item1) ?? new HashSet<string>();
            if (rows.Count == 0)
            {
                findings.Add(Draft(FindingKind.MissingInPacs, grant, null, new FindingDetails([], ExpectedOf(grant, cards, null), null)));
                continue;
            }

            var match = rows.FirstOrDefault(r => Differences(r, grant, cards, input.NativeValidity).Count == 0);
            if (match is not null)
            {
                inSync++;
            }
            else
            {
                match = rows[0];
                var details = new FindingDetails(
                    Differences(match, grant, cards, input.NativeValidity), ExpectedOf(grant, cards, match), ActualOf(match));
                findings.Add(Draft(FindingKind.Mismatch, grant, match, details));
            }

            foreach (var extra in rows.Where(r => !ReferenceEquals(r, match)))
            {
                compared++;
                findings.Add(Extra(FindingKind.ExtraInPacs, extra, null, input, knownPersons));
            }
        }

        foreach (var row in active.Where(r => r.PersonRef is null || rowsByKey.ContainsKey((r.PersonRef, r.VendorAccessLevelRef))))
        {
            var key = (row.PersonRef ?? string.Empty, row.VendorAccessLevelRef);
            if (row.PersonRef is not null && inFlight.Contains(key))
            {
                skippedKeys.Add(key);
                continue;
            }

            compared++;
            var expiredGrant = row.PersonRef is null ? null : expired.GetValueOrDefault(key);
            findings.Add(expiredGrant is not null
                ? Extra(FindingKind.ExpiredStillPresent, row, expiredGrant, input, knownPersons)
                : Extra(FindingKind.ExtraInPacs, row, null, input, knownPersons));
        }

        var ordered = findings
            .OrderBy(f => f.Kind)
            .ThenBy(f => f.PersonRef, StringComparer.Ordinal)
            .ThenBy(f => f.VendorAccessLevelRef, StringComparer.Ordinal)
            .ThenBy(f => f.PacsRef, StringComparer.Ordinal)
            .ToList();
        return new ReconciliationOutcome(ordered, compared, inSync, skippedKeys.Count, ignoredInactive);
    }

    private static (string, string) KeyOf(GrantState g) => (PersonRefOf(g.PersonId), g.VendorAccessLevelRef);

    private static string PersonRefOf(PersonId id) => id.Value.ToString();

    private static List<string> Differences(ExportedAssignment row, GrantState grant, IReadOnlySet<string> cards, bool nativeValidity)
    {
        var differences = new List<string>();
        if (row.CardNumber is null || !cards.Contains(row.CardNumber))
        {
            differences.Add("cardNumber");
        }

        if (nativeValidity)
        {
            if (!Near(row.ValidFrom, grant.RequestedStart))
            {
                differences.Add("validFrom");
            }

            if (!Near(row.ValidTo, grant.EffectiveEnd))
            {
                differences.Add("validTo");
            }
        }

        return differences;
    }

    /// <summary>The PACS holds one merged window per person and level (<see cref="AccessWindow.Current"/>), anchored on the grant that ends
    /// first.</summary>
    private static GrantState Merged(IEnumerable<GrantState> grants)
    {
        var list = grants.ToList();
        var anchor = list.OrderBy(g => g.EffectiveEnd).ThenBy(g => g.RequestedStart).First();
        var window = AccessWindow.Current(list.Select(g => new AccessWindow(g.RequestedStart, g.EffectiveEnd)));
        return window is { } w ? anchor with { RequestedStart = w.Start, EffectiveEnd = w.End } : anchor;
    }

    private static bool Near(DateTimeOffset? actual, DateTimeOffset expected) =>
        actual is { } a && (a - expected).Duration() <= DateTolerance;

    private static FindingValues ExpectedOf(GrantState grant, IReadOnlySet<string> cards, ExportedAssignment? row)
    {
        var card = row?.CardNumber is { } c && cards.Contains(c) ? c : cards.Order(StringComparer.Ordinal).FirstOrDefault();
        return new FindingValues(card is null ? null : CardNumberMask.Mask(card), grant.RequestedStart, grant.EffectiveEnd);
    }

    private static FindingValues ActualOf(ExportedAssignment row) =>
        new(row.CardNumber is null ? null : CardNumberMask.Mask(row.CardNumber), row.ValidFrom, row.ValidTo);

    private static FindingDraft Draft(FindingKind kind, GrantState grant, ExportedAssignment? row, FindingDetails details) =>
        new(kind, grant.PersonId, grant.AreaId, grant.EntitlementId, grant.FulfillmentId, row?.PacsRef,
            PersonRefOf(grant.PersonId), grant.VendorAccessLevelRef, row?.CardNumber, details);

    private static FindingDraft Extra(
        FindingKind kind, ExportedAssignment row, GrantState? grant, ReconciliationInput input, HashSet<string> knownPersons)
    {
        PersonId? personId = grant?.PersonId
            ?? (
                row.PersonRef is not null && knownPersons.Contains(row.PersonRef) && Guid.TryParse(row.PersonRef, out var id)
                    ? new PersonId(id)
                    : null);
        var areaId = grant?.AreaId ?? input.AreaByLevelRef[row.VendorAccessLevelRef];
        var expectedValues = grant is null ? null : new FindingValues(null, grant.RequestedStart, grant.EffectiveEnd);
        return new FindingDraft(kind, personId, areaId, grant?.EntitlementId, grant?.FulfillmentId, row.PacsRef, row.PersonRef,
            row.VendorAccessLevelRef, row.CardNumber, new FindingDetails([], expectedValues, ActualOf(row)));
    }
}
