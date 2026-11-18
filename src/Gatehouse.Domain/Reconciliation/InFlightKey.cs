namespace Gatehouse.Domain.Reconciliation;

/// <summary>A (person, access level) key with a change still on its way to the PACS; never a finding.</summary>
public sealed record InFlightKey(string PersonRef, string VendorAccessLevelRef);
