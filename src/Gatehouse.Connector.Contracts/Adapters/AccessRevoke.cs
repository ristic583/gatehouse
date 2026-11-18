using Gatehouse.Connector.Contracts.Events;

namespace Gatehouse.Connector.Contracts.Adapters;

public sealed record AccessRevoke(string PersonRef, string CardNumber, string VendorAccessLevelRef);
