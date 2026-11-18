using System.Threading.Channels;

namespace CloudPacs.Simulator;

/// <summary>Event source: creates vendor events and queues them for webhook delivery.</summary>
public sealed class CloudPacsEvents(TimeProvider time)
{
    public static readonly string[] Types =
    [
        "access.granted", "access.denied", "door.opened", "door.closed", "door.held_open", "door.forced", "door.unlocked", "door.relocked",
    ];

    private readonly Channel<SimEvent> _channel = Channel.CreateUnbounded<SimEvent>();
    private long _sequence;

    public ChannelReader<SimEvent> Reader => _channel.Reader;

    public SimEvent Emit(string type, string doorRef, string? cardNumber = null, string? externalRef = null, string? readerRef = null, string? direction = null)
    {
        var e = new SimEvent(Guid.CreateVersion7(), Interlocked.Increment(ref _sequence), type, doorRef, readerRef, cardNumber, externalRef, direction, time.GetUtcNow());
        _channel.Writer.TryWrite(e);
        return e;
    }
}
