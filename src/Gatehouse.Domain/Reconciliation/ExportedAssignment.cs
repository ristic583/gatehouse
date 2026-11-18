namespace Gatehouse.Domain.Reconciliation;

/// <summary>An <c>AccessAssignment</c> row of a PACS export. <see cref="CardNumber"/> is the full number.</summary>
public sealed record ExportedAssignment(
    string PacsRef,
    string? PersonRef,
    string? CardNumber,
    string VendorAccessLevelRef,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidTo,
    bool IsActive);
