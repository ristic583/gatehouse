using Gatehouse.Connector.Contracts.Events;

namespace Gatehouse.Connector.Contracts.Protocol;

public sealed record AckRequest(IReadOnlyList<CommandAcked> Acks);
