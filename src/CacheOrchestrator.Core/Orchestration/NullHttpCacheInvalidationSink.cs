namespace CacheOrchestrator.Orchestration;

/// <summary>No-op <see cref="IHttpCacheInvalidationSink"/> for hosts without Output Cache.</summary>
internal sealed class NullHttpCacheInvalidationSink : IHttpCacheInvalidationSink
{
    public static NullHttpCacheInvalidationSink Instance { get; } = new();

    private NullHttpCacheInvalidationSink()
    {
    }

    /// <inheritdoc />
    public ValueTask EvictByTagAsync(string tag, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;
}
