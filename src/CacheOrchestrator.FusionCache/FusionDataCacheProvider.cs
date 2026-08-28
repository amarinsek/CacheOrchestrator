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
    private readonly IFusionDomainSettingsProvider _fusionDomainSettings;
    private readonly ILogger<FusionDataCacheProvider> _logger;

    public FusionDataCacheProvider(
        IFusionCacheProvider fusionProvider,
        IOptionsMonitor<CacheOrchestratorOptions> options,
        IFusionDomainSettingsProvider fusionDomainSettings,
        ILogger<FusionDataCacheProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(fusionProvider);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(fusionDomainSettings);
        ArgumentNullException.ThrowIfNull(logger);

        _fusionProvider = fusionProvider;
        _options = options;
        _fusionDomainSettings = fusionDomainSettings;
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
        DomainFusionCacheSettings fusionSettings = _fusionDomainSettings.Get(request.DomainOptions.Domain);
        FusionCacheEntryOptions entryOptions = FusionEntryOptionsFactory.Create(request.DomainOptions, fusionSettings);
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
    public async ValueTask SetAsync<T>(
        DataCacheProviderRequest request,
        T value,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        IFusionCache fusion = _fusionProvider.GetCache(request.InstanceName);
        DomainFusionCacheSettings fusionSettings = _fusionDomainSettings.Get(request.DomainOptions.Domain);
        FusionCacheEntryOptions entryOptions = FusionEntryOptionsFactory.Create(request.DomainOptions, fusionSettings);
        string[] tags = request.Tags as string[] ?? [.. request.Tags];

        await fusion.SetAsync(
                request.Key,
                value,
                entryOptions,
                tags: tags,
                token: cancellationToken)
            .ConfigureAwait(false);

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("FusionDataCacheProvider Set Key={Key}", request.Key);
    }

    /// <inheritdoc />
    public async ValueTask InvalidateAsync(
        DataCacheInvalidationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        IEnumerable<string> instances = request.InstanceName is null
            ? _options.CurrentValue.DataCacheInstances.Keys
            : [request.InstanceName];

        foreach (string instanceName in instances)
        {
            IFusionCache fusion = _fusionProvider.GetCache(instanceName);
            foreach (string tag in request.Tags)
            {
                if (!string.IsNullOrWhiteSpace(tag))
                {
                    await fusion.RemoveByTagAsync(tag, token: cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }
}
