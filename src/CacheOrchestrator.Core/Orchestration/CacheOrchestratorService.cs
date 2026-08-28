using CacheOrchestrator.Configuration;
using CacheOrchestrator.Diagnostics;
using CacheOrchestrator.Entity;
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

        DataCacheProviderRequest providerRequest = CreateProviderRequest(opts, request);

        using Activity? activity = CacheOrchestratorActivitySource.Source.StartActivity("cache.orchestrator.get_or_create");
        activity?.SetTag("domain", opts.Domain);
        activity?.SetTag("provider", _dataCache.Name);

        try
        {
            // Store type is T? so null values can be cached when the caller uses a nullable T.
            DataCacheProviderResult<T?> result = await _dataCache.GetOrCreateAsync(
                    providerRequest,
                    factory,
                    cancellationToken)
                .ConfigureAwait(false);
            activity?.SetTag("cache.result", result.Outcome == DataCacheProviderOutcome.Materialized ? "miss" : "hit");
            return result.Value;
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

    /// <inheritdoc />
    public async ValueTask<FootprintCacheBox<T?>> GetOrCreateWithFootprintAsync<T>(
        CacheEntryRequest request,
        Func<CancellationToken, ValueTask<FootprintCacheBox<T?>>> factory,
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

            FootprintCacheBox<T?> uncached = await factory(cancellationToken).ConfigureAwait(false);
            return NormalizeBox(uncached);
        }

        DataCacheProviderRequest earlyRequest = CreateProviderRequest(opts, request);
        using Activity? activity = CacheOrchestratorActivitySource.Source.StartActivity("cache.orchestrator.get_or_create_footprint");
        activity?.SetTag("domain", opts.Domain);
        activity?.SetTag("provider", _dataCache.Name);

        try
        {
            DataCacheProviderResult<FootprintCacheBox<T?>> result = await _dataCache.GetOrCreateAsync(
                    earlyRequest,
                    async token =>
                    {
                        FootprintCacheBox<T?> produced = await factory(token).ConfigureAwait(false);
                        return NormalizeBox(produced);
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            FootprintCacheBox<T?> box = NormalizeBox(result.Value);

            // Refresh tags after miss when the factory expanded the footprint beyond early tags.
            if (result.Outcome == DataCacheProviderOutcome.Materialized)
            {
                IReadOnlyList<string> finalTags = BuildTags(opts, box.Footprint, request.AdditionalTags);
                DataCacheProviderRequest finalRequest = new()
                {
                    Key = earlyRequest.Key,
                    InstanceName = earlyRequest.InstanceName,
                    Tags = finalTags,
                    DomainOptions = opts
                };

                await _dataCache.SetAsync(finalRequest, box, cancellationToken).ConfigureAwait(false);
            }

            activity?.SetTag("cache.result", result.Outcome == DataCacheProviderOutcome.Materialized ? "miss" : "hit");
            return box;
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

    /// <inheritdoc />
    public async ValueTask<T?> GetOrCreateEntityAsync<T>(
        string domain,
        string logicalKey,
        EntityRef primary,
        Func<CancellationToken, ValueTask<T?>> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalKey);
        ArgumentNullException.ThrowIfNull(factory);
        EnsureUsablePrimary(primary);

        EntityFootprint early = new(primary);
        FootprintCacheBox<T?> box = await GetOrCreateWithFootprintAsync<T>(
                new CacheEntryRequest
                {
                    Domain = domain,
                    Key = logicalKey,
                    Footprint = early
                },
                async token =>
                {
                    T? value = await factory(token).ConfigureAwait(false);
                    return new FootprintCacheBox<T?>
                    {
                        Value = value,
                        IsMiss = value is null,
                        Footprint = early
                    };
                },
                cancellationToken)
            .ConfigureAwait(false);

        return box.IsMiss ? default : box.Value;
    }

    /// <inheritdoc />
    public async ValueTask<T?> GetOrCreateEntityAsync<T>(
        string domain,
        string logicalKey,
        EntityRef primary,
        Func<CancellationToken, ValueTask<EntityCache<T>>> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalKey);
        ArgumentNullException.ThrowIfNull(factory);
        EnsureUsablePrimary(primary);

        FootprintCacheBox<T?> box = await GetOrCreateWithFootprintAsync<T>(
                new CacheEntryRequest
                {
                    Domain = domain,
                    Key = logicalKey,
                    Footprint = new EntityFootprint(primary)
                },
                async token =>
                {
                    EntityCache<T> produced = await factory(token).ConfigureAwait(false);
                    ArgumentNullException.ThrowIfNull(produced);
                    EntityFootprint full = (produced.Footprint ?? EntityFootprint.Empty).WithPrimary(primary);
                    return new FootprintCacheBox<T?>
                    {
                        Value = produced.IsMiss ? default : produced.Value,
                        IsMiss = produced.IsMiss,
                        Footprint = full
                    };
                },
                cancellationToken)
            .ConfigureAwait(false);

        return box.IsMiss ? default : box.Value;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<T>> GetOrCreateEntitySetAsync<T>(
        string domain,
        string logicalKey,
        string entityKind,
        Func<CancellationToken, ValueTask<EntitySet<T>>> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityKind);
        ArgumentNullException.ThrowIfNull(factory);

        string normalizedKind = DomainName.NormalizeEntityKind(entityKind);
        if (string.IsNullOrEmpty(normalizedKind))
            throw new ArgumentException("Entity kind must contain usable characters after normalization.", nameof(entityKind));

        // Early tags: domain + entitykind (members arrive from the factory footprint).
        EntityFootprint early = new(
            primary: null,
            members: null,
            dependsOn: null,
            aliases: null);
        // EntityFootprint.Empty has no kind tag — pass kind via AdditionalTags for the early request.
        string kindTag = CacheTags.EntityKind(DomainName.Normalize(domain), normalizedKind);

        FootprintCacheBox<IReadOnlyList<T>?> box = await GetOrCreateWithFootprintAsync<IReadOnlyList<T>>(
                new CacheEntryRequest
                {
                    Domain = domain,
                    Key = logicalKey,
                    Footprint = early,
                    AdditionalTags = [kindTag]
                },
                async token =>
                {
                    EntitySet<T> produced = await factory(token).ConfigureAwait(false);
                    ArgumentNullException.ThrowIfNull(produced);
                    EntityFootprint footprint = produced.BuildFootprint(normalizedKind);
                    return new FootprintCacheBox<IReadOnlyList<T>?>
                    {
                        Value = produced.Value,
                        IsMiss = false,
                        Footprint = footprint
                    };
                },
                cancellationToken)
            .ConfigureAwait(false);

        return box.Value ?? [];
    }

    /// <summary>
    /// Physical key: <c>{domain}:{versionHex}:{logicalKey}</c> (Http-free / library path).
    /// </summary>
    internal static string BuildPhysicalKey(DomainCacheOptions opts, string logicalKey)
        => string.Concat(opts.Domain, ":", opts.VersionHex, ":", logicalKey);

    internal static IReadOnlyList<string> BuildTags(
        DomainCacheOptions options,
        EntityFootprint? footprint,
        IReadOnlyList<string>? additionalTags)
    {
        string domainTag = options.DomainTag;
        bool hasFootprint = footprint is not null && !ReferenceEquals(footprint, EntityFootprint.Empty);
        if (!hasFootprint && additionalTags is not { Count: > 0 })
            return options.DomainTags;

        if (additionalTags is not { Count: > 0 }
            && footprint!.Primary is { } primary
            && footprint.Members.Count == 0
            && footprint.DependsOn.Count == 0
            && footprint.Aliases.Count == 0)
        {
            return
            [
                domainTag,
                CacheTags.Entity(options.Domain, primary.EntityKind, primary.ResourceId),
                CacheTags.EntityKind(options.Domain, primary.EntityKind)
            ];
        }

        if (!hasFootprint && additionalTags is { Count: 1 })
        {
            string? additionalTag = additionalTags[0];
            if (string.IsNullOrWhiteSpace(additionalTag)
                || string.Equals(additionalTag, domainTag, StringComparison.Ordinal))
            {
                return options.DomainTags;
            }

            return [domainTag, additionalTag];
        }

        List<string> tags = hasFootprint
            ? [.. footprint!.ToTags(options.Domain)]
            : [domainTag];

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

        return [.. tags];
    }

    private static DataCacheProviderRequest CreateProviderRequest(DomainCacheOptions opts, CacheEntryRequest request)
    {
        string physicalKey = request.KeyIsPhysical
            ? request.Key
            : BuildPhysicalKey(opts, request.Key);

        return new DataCacheProviderRequest
        {
            Key = physicalKey,
            InstanceName = opts.DataCacheInstanceName,
            Tags = BuildTags(opts, request.Footprint, request.AdditionalTags),
            DomainOptions = opts
        };
    }

    private static FootprintCacheBox<T?> NormalizeBox<T>(FootprintCacheBox<T?>? box)
    {
        if (box is null)
        {
            return new FootprintCacheBox<T?>
            {
                Value = default,
                IsMiss = true,
                Footprint = EntityFootprint.Empty
            };
        }

        if (box.Footprint is not null)
            return box;

        return new FootprintCacheBox<T?>
        {
            Value = box.Value,
            IsMiss = box.IsMiss,
            Footprint = box.Footprint ?? EntityFootprint.Empty
        };
    }

    private static void EnsureUsablePrimary(EntityRef primary)
    {
        string kind = DomainName.NormalizeEntityKind(primary.EntityKind);
        string id = DomainName.NormalizeResourceId(primary.ResourceId);
        if (string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(id))
        {
            throw new ArgumentException(
                "Primary entity kind and id must contain usable characters after normalization.",
                nameof(primary));
        }
    }
}
