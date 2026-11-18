using Gatehouse.Connector.Contracts.Events;

namespace Gatehouse.Connector.Contracts.Adapters;

public sealed record AccessGrant(
    string PersonRef, string CardNumber, string VendorAccessLevelRef, DateTimeOffset ValidFrom, DateTimeOffset ValidTo);
