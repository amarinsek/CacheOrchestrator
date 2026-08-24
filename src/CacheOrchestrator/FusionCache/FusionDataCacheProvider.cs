using CacheOrchestrator.Configuration;
using CacheOrchestrator.Orchestration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;

namespace CacheOrchestrator.FusionCache;

/// <summary>
/// <see cref="IDataCacheProvider"/> backed by ZiggyCreatures FusionCache.
/// </summary>
internal sealed class FusionDataCacheProvider : IDataCacheProvider
{
    private readonly IFusionCacheProvider _fusionProvider;
    private readonly IOptionsMonitor<CacheOrchestratorOptions> _options;
    private readonly ILogger<FusionDataCacheProvider> _logger;

    public FusionDataCacheProvider(
        IFusionCacheProvider fusionProvider,
        IOptionsMonitor<CacheOrchestratorOptions> options,
        ILogger<FusionDataCacheProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(fusionProvider);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _fusionProvider = fusionProvider;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "FusionCache";

    /// <inheritdoc />
    public async ValueTask<T> GetOrCreateAsync<T>(
        DataCacheProviderRequest request,
        Func<CancellationToken, ValueTask<T>> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(factory);

        IFusionCache fusion = _fusionProvider.GetCache(request.InstanceName);
        FusionCacheEntryOptions entryOptions = FusionEntryOptionsFactory.Create(request.DomainOptions);
        string[] tags = request.Tags as string[] ?? [.. request.Tags];

        T result = await fusion.GetOrSetAsync<T>(
                request.Key,
                async (_, token) => await factory(token).ConfigureAwait(false),
                entryOptions,
                tags: tags,
                token: cancellationToken)
            .ConfigureAwait(false);

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("FusionDataCacheProvider GetOrCreate Key={Key}", request.Key);

        return result;
    }

    /// <inheritdoc />
    public async ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        foreach (string instanceName in _options.CurrentValue.FusionCacheInstances.Keys)
            await RemoveByTagAsync(instanceName, tag, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask RemoveByTagAsync(
        string instanceName,
        string tag,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        IFusionCache fusion = _fusionProvider.GetCache(instanceName);
        await fusion.RemoveByTagAsync(tag, token: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask RemoveByTagsAsync(
        IEnumerable<string> tags,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tags);

        foreach (string? tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag))
                continue;
            await RemoveByTagAsync(tag, cancellationToken).ConfigureAwait(false);
        }
    }
}
