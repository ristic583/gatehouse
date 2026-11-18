using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Gatehouse.Application.Common.Diagnostics;

/// <summary>
/// The platform's OpenTelemetry source and meter ("Gatehouse"). Tags carry ids only, never names, emails or card numbers.
/// </summary>
public static class GatehouseTelemetry
{
    public const string Name = "Gatehouse";

    public static readonly ActivitySource Source = new(Name);
    public static readonly Meter Meter = new(Name);

    /// <summary>Seconds from command creation to the agent's applied ack.</summary>
    public static readonly Histogram<double> ConfirmLatency = Meter.CreateHistogram<double>(
        "gatehouse.fulfillment.confirm_latency", "s", "Command created → applied ack");

    /// <summary>Queued + delivered commands per PACS instance, sampled at each poll.</summary>
    public static readonly Gauge<long> QueueDepth = Meter.CreateGauge<long>(
        "gatehouse.connector.queue_depth", "{command}", "Open connector commands per instance");

    public static readonly Counter<long> DoorEventsIngested = Meter.CreateCounter<long>(
        "gatehouse.door_events.ingested", "{event}", "Door events stored");

    /// <summary>The current W3C <c>traceparent</c>, or null when no activity is running.</summary>
    public static string? CurrentTraceParent() => Activity.Current is { IdFormat: ActivityIdFormat.W3C } a ? a.Id : null;

    /// <summary>Starts a span whose parent is a stored <c>traceparent</c> (or the current activity when it cannot be parsed).</summary>
    public static Activity? StartWithParent(string name, string? traceParent, ActivityKind kind = ActivityKind.Internal) =>
        traceParent is not null && ActivityContext.TryParse(traceParent, null, out var parent)
            ? Source.StartActivity(name, kind, parent)
            : Source.StartActivity(name, kind);
}
