using Gatehouse.Connector.Contracts.Events;

namespace Gatehouse.Connector.Contracts.Adapters;

public sealed record CardStateChange(string PersonRef, string CardNumber, CardTargetState TargetState);
