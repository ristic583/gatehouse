using Gatehouse.Connector.Contracts.Adapters;

namespace Gatehouse.Connector.Contracts.Events;

/// <summary><c>RefusedRemovals</c> null = the agent does not report refusals (older agents); empty = none pending.</summary>
public sealed record Heartbeat(
    string AgentVersion,
    AdapterInfo Adapter,
    PacsCapabilities Capabilities,
    int QueueDepth,
    bool PacsReachable,
    DateTimeOffset AgentTime,
    IReadOnlyList<RefusedRemoval>? RefusedRemovals = null);
