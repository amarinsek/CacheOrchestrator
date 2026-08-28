using CacheOrchestrator.Orchestration;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace CacheOrchestrator.HybridCache;

/// <summary>
/// <see cref="IDataCacheProvider"/> backed by Microsoft <see cref="HybridCache"/>.
/// </summary>
/// <remarks>
/// Maps Core <c>DataCache.TtlSeconds</c> (resolved <c>DataCacheTtl</c>) to <see cref="HybridCacheEntryOptions.Expiration"/>.
/// Fusion-only knobs (fail-safe, soft/hard split, eager refresh, factory timeouts, backplane)
/// are not applied. Named data-cache instances are ignored — a single DI <see cref="HybridCache"/>
/// is used for all domains.
/// </remarks>
internal sealed class HybridDataCacheProvider : IDataCacheProvider
{
    private readonly Microsoft.Extensions.Caching.Hybrid.HybridCache _cache;
    private readonly ILogger<HybridDataCacheProvider> _logger;

    public HybridDataCacheProvider(
        Microsoft.Extensions.Caching.Hybrid.HybridCache cache,
        ILogger<HybridDataCacheProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(logger);

        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "HybridCache";

    /// <inheritdoc />
    public async ValueTask<T> GetOrCreateAsync<T>(
        DataCacheProviderRequest request,
        Func<CancellationToken, ValueTask<T>> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(factory);

        if (!string.Equals(request.InstanceName, "default", StringComparison.OrdinalIgnoreCase)
            && _logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "HybridCache provider ignores data-cache instance '{Instance}'; using the single DI HybridCache.",
                request.InstanceName);
        }

        // Fusion MaxItemBytes / fail-safe / hard TTL / eager refresh are not mapped (capability subset).
        HybridCacheEntryOptions entryOptions = new()
        {
            Expiration = request.DomainOptions.DataCacheTtl,
            LocalCacheExpiration = request.DomainOptions.DataCacheTtl,
        };

        string[] tags = request.Tags as string[] ?? [.. request.Tags];

        // Prefer the (key, state, factory) overload — a bare Func<CancellationToken, ValueTask<T>>
        // can bind to the state overload with the factory as TState.
        T result = await _cache.GetOrCreateAsync(
                request.Key,
                factory,
                static async (f, cancel) => await f(cancel).ConfigureAwait(false),
                entryOptions,
                tags,
                cancellationToken)
            .ConfigureAwait(false);

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("HybridDataCacheProvider GetOrCreate Key={Key}", request.Key);

        return result;
    }

    /// <inheritdoc />
    public async ValueTask SetAsync<T>(
        DataCacheProviderRequest request,
        T value,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!string.Equals(request.InstanceName, "default", StringComparison.OrdinalIgnoreCase)
            && _logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "HybridCache provider ignores data-cache instance '{Instance}'; using the single DI HybridCache.",
                request.InstanceName);
        }

        HybridCacheEntryOptions entryOptions = new()
        {
            Expiration = request.DomainOptions.DataCacheTtl,
            LocalCacheExpiration = request.DomainOptions.DataCacheTtl,
        };

        string[] tags = request.Tags as string[] ?? [.. request.Tags];

        await _cache.SetAsync(
                request.Key,
                value,
                entryOptions,
                tags,
                cancellationToken)
            .ConfigureAwait(false);

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("HybridDataCacheProvider Set Key={Key}", request.Key);
    }

    /// <inheritdoc />
    public async ValueTask InvalidateAsync(
        DataCacheInvalidationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        foreach (string tag in request.Tags)
        {
            if (!string.IsNullOrWhiteSpace(tag))
            {
                await _cache.RemoveByTagAsync(tag, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
