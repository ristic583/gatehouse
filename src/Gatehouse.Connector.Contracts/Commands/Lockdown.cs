using Gatehouse.Connector.Contracts.Adapters;

namespace Gatehouse.Connector.Contracts.Commands;

public sealed record Lockdown(string[] PacsDoorRefs, LockdownAction Action, string Reason);
