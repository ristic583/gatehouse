using Gatehouse.Domain.Sites;

namespace Gatehouse.Domain.Reconciliation;

/// <summary>
/// Everything <see cref="ReconciliationComparer"/> needs. <see cref="ActiveCardsByPersonRef"/> holds every known person of the
/// tenant that matters for the export (an empty set = known person without an active card); <see cref="AreaByLevelRef"/> is the
/// export scope (the instance's mapped access levels).
/// </summary>
public sealed record ReconciliationInput(
    DateTimeOffset CapturedAt,
    bool NativeValidity,
    IReadOnlyList<GrantState> Grants,
    IReadOnlyList<InFlightKey> InFlight,
    IReadOnlyDictionary<string, IReadOnlySet<string>> ActiveCardsByPersonRef,
    IReadOnlyDictionary<string, AreaId> AreaByLevelRef,
    IReadOnlyList<ExportedAssignment> Rows);
