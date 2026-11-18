namespace Gatehouse.Connector.Contracts.Commands;

public sealed record RevokeAccess(string PersonRef, string CardNumber, string VendorAccessLevelRef, RevocationKind Kind);
