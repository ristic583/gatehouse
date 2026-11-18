using Gatehouse.Connector.Contracts.Adapters;

namespace Gatehouse.Connector.Contracts.Commands;

public sealed record SuspendCard(string PersonRef, string CardNumber, CardTargetState TargetState, string Reason);
