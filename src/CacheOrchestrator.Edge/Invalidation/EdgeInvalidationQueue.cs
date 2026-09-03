using System.Threading.Channels;

namespace CacheOrchestrator.Edge.Invalidation;

/// <summary>One provider-neutral invalidation job containing only opaque projected tags.</summary>
public sealed record EdgeInvalidationJob(string InstanceName, string ProviderName, IReadOnlyList<string> Tags);

/// <summary>Queue boundary for edge invalidation; replace it to use a durable outbox.</summary>
public interface IEdgeInvalidationQueue
{
    /// <summary>Enqueues one invalidation job without performing provider network I/O.</summary>
    ValueTask EnqueueAsync(EdgeInvalidationJob job, CancellationToken cancellationToken);
}

internal sealed class EdgeInvalidationChannel
{
    public EdgeInvalidationChannel(int capacity)
    {
        Channel = System.Threading.Channels.Channel.CreateBounded<EdgeInvalidationJob>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public Channel<EdgeInvalidationJob> Channel { get; }
}

internal sealed class ChannelEdgeInvalidationQueue : IEdgeInvalidationQueue
{
    private readonly EdgeInvalidationChannel _channel;

    public ChannelEdgeInvalidationQueue(EdgeInvalidationChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        _channel = channel;
    }

    public ValueTask EnqueueAsync(EdgeInvalidationJob job, CancellationToken cancellationToken) =>
        _channel.Channel.Writer.WriteAsync(job, cancellationToken);
}
