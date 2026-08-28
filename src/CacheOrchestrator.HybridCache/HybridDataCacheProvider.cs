using CacheOrchestrator.Configuration;
using CacheOrchestrator.Orchestration;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    private readonly IOptionsMonitor<CacheOrchestratorOptions> _options;

    public HybridDataCacheProvider(
        Microsoft.Extensions.Caching.Hybrid.HybridCache cache,
        IOptionsMonitor<CacheOrchestratorOptions> options,
        ILogger<HybridDataCacheProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _cache = cache;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "HybridCache";

    /// <inheritdoc />
    public async ValueTask<DataCacheProviderResult<T>> GetOrCreateAsync<T>(
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

        string physicalKey = Prefix(request.DomainOptions.DataCacheNamespace, request.Key);
        string[] tags = PrefixTags(request.DomainOptions.DataCacheNamespace, request.Tags);

        // Prefer the (key, state, factory) overload — a bare Func<CancellationToken, ValueTask<T>>
        // can bind to the state overload with the factory as TState.
        var materializationId = Guid.NewGuid();
        HybridProviderCacheEntry<T> entry = await _cache.GetOrCreateAsync(
                physicalKey,
                factory,
                async (f, cancel) => new HybridProviderCacheEntry<T>
                {
                    Value = await f(cancel).ConfigureAwait(false),
                    MaterializationId = materializationId
                },
                entryOptions,
                tags,
                cancellationToken)
            .ConfigureAwait(false);

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("HybridDataCacheProvider GetOrCreate Key={Key}", physicalKey);

        DataCacheProviderOutcome outcome = entry.MaterializationId == materializationId
            ? DataCacheProviderOutcome.Materialized
            : DataCacheProviderOutcome.Cached;
        return new DataCacheProviderResult<T>(entry.Value, outcome);
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

        string physicalKey = Prefix(request.DomainOptions.DataCacheNamespace, request.Key);
        string[] tags = PrefixTags(request.DomainOptions.DataCacheNamespace, request.Tags);

        await _cache.SetAsync(
                physicalKey,
                new HybridProviderCacheEntry<T>
                {
                    Value = value,
                    MaterializationId = Guid.NewGuid()
                },
                entryOptions,
                tags,
                cancellationToken)
            .ConfigureAwait(false);

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("HybridDataCacheProvider Set Key={Key}", physicalKey);
    }

    /// <inheritdoc />
    public async ValueTask InvalidateAsync(
        DataCacheInvalidationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string cacheNamespace = ResolveNamespace(request.InstanceName);
        foreach (string tag in request.Tags)
        {
            if (!string.IsNullOrWhiteSpace(tag))
            {
                await _cache.RemoveByTagAsync(Prefix(cacheNamespace, tag), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private string ResolveNamespace(string? instanceName)
    {
        string name = string.IsNullOrWhiteSpace(instanceName) ? "default" : instanceName;
        CacheOrchestratorOptions current = _options.CurrentValue;
        CacheOrchestratorOptions.DataCacheInstanceOptions instance = current.DataCacheInstances.TryGetValue(
            name,
            out CacheOrchestratorOptions.DataCacheInstanceOptions? configured)
            ? configured
            : new CacheOrchestratorOptions.DataCacheInstanceOptions();
        return instance.GetNamespace(name, current);
    }

    private static string[] PrefixTags(string cacheNamespace, IReadOnlyList<string> tags)
    {
        string[] result = new string[tags.Count];
        for (int i = 0; i < tags.Count; i++)
            result[i] = Prefix(cacheNamespace, tags[i]);
        return result;
    }

    private static string Prefix(string cacheNamespace, string value) =>
        string.IsNullOrWhiteSpace(cacheNamespace)
            ? value
            : Uri.EscapeDataString(cacheNamespace) + ":" + value;
}
