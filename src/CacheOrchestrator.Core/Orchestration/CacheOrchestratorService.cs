using CacheOrchestrator.Configuration;
using CacheOrchestrator.Diagnostics;
using CacheOrchestrator.FusionCache;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace CacheOrchestrator.Orchestration;

/// <summary>
/// Default <see cref="ICacheOrchestrator"/>: domain policy + Version keying + tags, then
/// <see cref="IDataCacheProvider"/>.
/// </summary>
internal sealed class CacheOrchestratorService : ICacheOrchestrator
{
    private readonly IDomainCacheOptionsProvider _domainOptions;
    private readonly IDataCacheProvider _dataCache;
    private readonly ILogger<CacheOrchestratorService> _logger;

    public CacheOrchestratorService(
        IDomainCacheOptionsProvider domainOptions,
        IDataCacheProvider dataCache,
        ILogger<CacheOrchestratorService> logger)
    {
        ArgumentNullException.ThrowIfNull(domainOptions);
        ArgumentNullException.ThrowIfNull(dataCache);
        ArgumentNullException.ThrowIfNull(logger);

        _domainOptions = domainOptions;
        _dataCache = dataCache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<T?> GetOrCreateAsync<T>(
        CacheEntryRequest request,
        Func<CancellationToken, ValueTask<T?>> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Key);

        DomainCacheOptions opts = _domainOptions.GetOrCreateDomainOptions(request.Domain);

        if (!opts.DataCacheEnabled)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("Data cache off for domain {Domain}; factory runs uncached.", opts.Domain);

            return await factory(cancellationToken).ConfigureAwait(false);
        }

        string physicalKey = BuildPhysicalKey(opts, request.Key);
        IReadOnlyList<string> tags = BuildTags(opts.Domain, request.Footprint, request.AdditionalTags);

        DataCacheProviderRequest providerRequest = new()
        {
            Key = physicalKey,
            InstanceName = opts.FusionCacheInstanceName,
            Tags = tags,
            DomainOptions = opts
        };

        using Activity? activity = CacheOrchestratorActivitySource.Source.StartActivity("cache.orchestrator.get_or_create");
        activity?.SetTag("domain", opts.Domain);
        activity?.SetTag("provider", _dataCache.Name);

        try
        {
            // Store type is T? so null values can be cached when the caller uses a nullable T.
            T? value = await _dataCache.GetOrCreateAsync(
                    providerRequest,
                    factory,
                    cancellationToken)
                .ConfigureAwait(false);
            activity?.SetTag("cache.result", "ok");
            return value;
        }
        catch (OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "canceled");
            throw;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Physical key: <c>{domain}:{versionHex}:{logicalKey}</c> (Http-free / library path).
    /// </summary>
    internal static string BuildPhysicalKey(DomainCacheOptions opts, string logicalKey)
        => string.Concat(opts.Domain, ":", opts.VersionHex, ":", logicalKey);

    internal static IReadOnlyList<string> BuildTags(
        string normalizedDomain,
        EntityFootprint? footprint,
        IReadOnlyList<string>? additionalTags)
    {
        List<string> tags = footprint is null || ReferenceEquals(footprint, EntityFootprint.Empty)
            ? [CacheTags.Domain(normalizedDomain)]
            : [.. footprint.ToTags(normalizedDomain)];

        if (additionalTags is { Count: > 0 })
        {
            HashSet<string> seen = new(tags, StringComparer.Ordinal);
            for (int i = 0; i < additionalTags.Count; i++)
            {
                string? tag = additionalTags[i];
                if (string.IsNullOrWhiteSpace(tag))
                    continue;
                if (seen.Add(tag))
                    tags.Add(tag);
            }
        }

        return tags;
    }
}
