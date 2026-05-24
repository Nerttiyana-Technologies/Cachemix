using System.Threading.Channels;

namespace Cachemix.Core;

/// <summary>
/// The bounded, lock-light channel that cache decorators publish telemetry
/// signals to. When the channel is full, new signals are dropped rather than
/// blocking the caller — telemetry must never slow the host's cache.
/// </summary>
internal sealed class CacheEventPipeline
{
    private readonly Channel<CacheSignal> _channel;

    /// <summary>Creates a pipeline with the given bounded capacity.</summary>
    /// <param name="capacity">The maximum number of buffered signals.</param>
    public CacheEventPipeline(int capacity)
    {
        if (capacity < 1)
        {
            capacity = 1;
        }

        _channel = Channel.CreateBounded<CacheSignal>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    /// <summary>The reader drained by the telemetry processor.</summary>
    public ChannelReader<CacheSignal> Reader => _channel.Reader;

    /// <summary>
    /// Publishes a signal. Never blocks; if the channel is full the signal is
    /// silently dropped so the calling cache operation is never delayed.
    /// </summary>
    /// <param name="signal">The signal to publish.</param>
    public void Publish(in CacheSignal signal) => _channel.Writer.TryWrite(signal);

    /// <summary>Marks the channel complete so the processor can drain and stop.</summary>
    public void Complete() => _channel.Writer.TryComplete();
}
