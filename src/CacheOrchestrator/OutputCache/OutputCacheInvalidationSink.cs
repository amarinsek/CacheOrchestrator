using CacheOrchestrator.Orchestration;
using Microsoft.AspNetCore.OutputCaching;

namespace CacheOrchestrator.OutputCache;

/// <summary>
/// <see cref="IHttpCacheInvalidationSink"/> backed by ASP.NET Core <see cref="IOutputCacheStore"/>.
/// </summary>
internal sealed class OutputCacheInvalidationSink : IHttpCacheInvalidationSink
{
    private readonly IOutputCacheStore _store;

    public OutputCacheInvalidationSink(IOutputCacheStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    public ValueTask EvictByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        return _store.EvictByTagAsync(tag, cancellationToken);
    }
}
