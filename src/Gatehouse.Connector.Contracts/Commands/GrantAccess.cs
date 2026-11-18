namespace Gatehouse.Connector.Contracts.Commands;

public sealed record GrantAccess(
    string PersonRef, string CardNumber, string VendorAccessLevelRef, DateTimeOffset ValidFrom, DateTimeOffset ValidTo);
