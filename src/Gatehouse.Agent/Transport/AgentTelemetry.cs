using System.Diagnostics;
using Gatehouse.Connector.Contracts.Protocol;

namespace Gatehouse.Agent.Transport;

/// <summary>Agent spans ("Gatehouse.Agent"): one per executed command, child of the trace that created the command (envelope
/// <c>traceParent</c>).</summary>
public static class AgentTelemetry
{
    public const string SourceName = "Gatehouse.Agent";

    public static readonly ActivitySource Source = new(SourceName);

    public static Activity? StartExecute(CommandEnvelope command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var name = $"Agent.Execute {command.Type}";
        var activity = command.TraceParent is not null && ActivityContext.TryParse(command.TraceParent, null, out var parent)
            ? Source.StartActivity(name, ActivityKind.Consumer, parent)
            : Source.StartActivity(name, ActivityKind.Consumer);
        activity?.SetTag("gatehouse.command_id", command.CommandId);
        activity?.SetTag("gatehouse.pacs_instance", command.PacsInstanceId);
        return activity;
    }
}
