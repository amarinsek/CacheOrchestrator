using CacheOrchestrator.Admin;
using CacheOrchestrator.Configuration;
using CacheOrchestrator.Diagnostics;
using CacheOrchestrator.OutputCache;
using CacheOrchestrator.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using ZiggyCreatures.Caching.Fusion;

namespace CacheOrchestrator.FusionCache;

/// <summary>
/// Default <see cref="IDomainFusionCache"/> implementation backed by ZiggyCreatures FusionCache.
/// </summary>
internal sealed class DomainFusionCacheService : IDomainFusionCache
{
    private readonly IFusionCacheProvider _fusionProvider;
    private readonly IDomainCacheOptionsProvider _domainConfig;
    private readonly IDomainKeyGenerator _keyGenerator;
    private readonly ILogger<DomainFusionCacheService> _logger;
    private readonly IAdminStatsCollector _adminStats;

    /// <summary>
    /// Initializes a new instance of the <see cref="DomainFusionCacheService"/> class.
    /// </summary>
    public DomainFusionCacheService(
        IFusionCacheProvider fusionProvider,
        IDomainCacheOptionsProvider domainConfig,
        IDomainKeyGenerator keyGenerator,
        ILogger<DomainFusionCacheService> logger,
        IAdminStatsCollector? adminStats = null)
    {
        ArgumentNullException.ThrowIfNull(fusionProvider);
        ArgumentNullException.ThrowIfNull(domainConfig);
        ArgumentNullException.ThrowIfNull(keyGenerator);
        ArgumentNullException.ThrowIfNull(logger);

        _fusionProvider = fusionProvider;
        _domainConfig = domainConfig;
        _keyGenerator = keyGenerator;
        _logger = logger;
        _adminStats = adminStats ?? NoOpAdminStatsCollector.Instance;
    }

    /// <inheritdoc />
    public Task<T> GetOrSetAsync<T>(
        HttpContext http,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken = default)
        => GetOrSetCoreAsync(http, domain: null, entityKind: null, resourceId: null, factory, cancellationToken);

    /// <inheritdoc />
    public Task<T> GetOrSetAsync<T>(
        HttpContext http,
        string domain,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        return GetOrSetCoreAsync(http, domain, entityKind: null, resourceId: null, factory, cancellationToken);
    }

    /// <inheritdoc />
    public Task<T?> GetOrSetEntityAsync<T>(
        HttpContext http,
        Func<CancellationToken, Task<T?>> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(factory);
        EnsureRequestHasPrimaryIdentity(http);

        return GetOrSetFootprintAsync(
            http,
            domain: null,
            useEntityKey: true,
            async token =>
            {
                T? value = await factory(token).ConfigureAwait(false);
                return new FootprintCacheBox<T?>
                {
                    Value = value,
                    IsMiss = value is null,
                    Footprint = EntityFootprint.Empty
                };
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<T?> GetOrSetEntityAsync<T>(
        HttpContext http,
        Func<CancellationToken, Task<EntityCache<T>>> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(factory);
        EnsureRequestHasPrimaryIdentity(http);

        return GetOrSetFootprintAsync(
            http,
            domain: null,
            useEntityKey: true,
            async token =>
            {
                EntityCache<T> produced = await factory(token).ConfigureAwait(false);
                ArgumentNullException.ThrowIfNull(produced);
                return new FootprintCacheBox<T?>
                {
                    Value = produced.IsMiss ? default : produced.Value,
                    IsMiss = produced.IsMiss,
                    Footprint = produced.Footprint
                };
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<T>> GetOrSetEntitySetAsync<T>(
        HttpContext http,
        Func<CancellationToken, Task<EntitySet<T>>> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(factory);

        string? kind = TryGetRequestEntityKind(http);
        if (string.IsNullOrEmpty(kind))
        {
            throw new InvalidOperationException(
                "Entity kind is not on the request. Use [CacheDomain(domain, entityKind)] / " +
                "CacheOutputWithDomain(domain, entityKind), or SetEntityIdentity before GetOrSetEntitySetAsync.");
        }

        IReadOnlyList<T>? list = await GetOrSetFootprintAsync(
                http,
                domain: null,
                useEntityKey: false,
                async token =>
                {
                    EntitySet<T> produced = await factory(token).ConfigureAwait(false);
                    ArgumentNullException.ThrowIfNull(produced);
                    EntityFootprint footprint = produced.BuildFootprint(kind);
                    return new FootprintCacheBox<IReadOnlyList<T>?>
                    {
                        Value = produced.Value,
                        IsMiss = false,
                        Footprint = footprint
                    };
                },
                cancellationToken)
            .ConfigureAwait(false);

        return list ?? [];
    }

    /// <inheritdoc />
    public void SetEntityIdentity(HttpContext http, string entityKind, string resourceId)
    {
        ArgumentNullException.ThrowIfNull(http);
        EnsureUsableEntityIdentity(entityKind, resourceId);

        ICacheOrchestratorFeature feature = CacheOrchestratorFeatureAccessor.GetOrCreate(http);
        feature.EntityKind = DomainName.NormalizeEntityKind(entityKind);
        feature.ResourceId = DomainName.NormalizeResourceId(resourceId);
    }

    /// <inheritdoc />
    [Obsolete("Use GetOrSetEntityAsync(http, factory). Identity comes from CacheOutputWithDomain / [CacheDomain] or SetEntityIdentity.")]
    public Task<T> GetOrSetEntityAsync<T>(
        HttpContext http,
        string entityKind,
        string resourceId,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken = default)
    {
        EnsureUsableEntityIdentity(entityKind, resourceId);
        return GetOrSetCoreAsync(http, domain: null, entityKind, resourceId, factory, cancellationToken);
    }

    /// <inheritdoc />
    [Obsolete("Use GetOrSetEntityAsync(http, factory). Identity comes from CacheOutputWithDomain / [CacheDomain] or SetEntityIdentity.")]
    public Task<T> GetOrSetEntityAsync<T>(
        HttpContext http,
        string domain,
        string entityKind,
        string resourceId,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        EnsureUsableEntityIdentity(entityKind, resourceId);
        return GetOrSetCoreAsync(http, domain, entityKind, resourceId, factory, cancellationToken);
    }

    private async Task<T?> GetOrSetFootprintAsync<T>(
        HttpContext http,
        string? domain,
        bool useEntityKey,
        Func<CancellationToken, Task<FootprintCacheBox<T?>>> factory,
        CancellationToken cancellationToken)
    {
        DomainCacheOptions? opts = ResolveDomainOptions(http, domain);
        if (opts is null)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(
                    "FusionCache skipped: no domain resolved for {Method} {Path}. " +
                    "Factory runs uncached. Use .CacheOutputWithDomain / [CacheDomain], " +
                    "GetOrSetAsync(http, domain, factory), or EnsureDomainOptions.",
                    http.Request.Method,
                    http.Request.Path.Value);
            }

            FootprintCacheBox<T?> unresolved = await factory(cancellationToken).ConfigureAwait(false);
            EntityFootprint staged = WithRequestPrimary(http, unresolved.Footprint);
            EntityFootprintStaging.Stage(http, staged);
            SetData(http, DataCacheResult.Unresolved);
            CacheOrchestratorMetrics.RecordFusion("_", "unresolved", durationMs: null, null, resultSizeBytes: null);
            return unresolved.IsMiss ? default : unresolved.Value;
        }

        if (!opts.FusionCacheEnabled)
        {
            FootprintCacheBox<T?> off = await factory(cancellationToken).ConfigureAwait(false);
            EntityFootprint staged = WithRequestPrimary(http, off.Footprint);
            EntityFootprintStaging.Stage(http, staged);
            SetData(http, DataCacheResult.Off);
            RecordFusionAndAdmin(http, opts.Domain, opts.Domain, "off", null, null);
            return off.IsMiss ? default : off.Value;
        }

        if (opts.FusionRespectAuthBypass && DomainAuthEvaluator.ShouldBypassForAuth(http, opts))
        {
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("FusionCache skipped due to auth bypass (FusionRespectAuthBypass)");

            FootprintCacheBox<T?> bypass = await factory(cancellationToken).ConfigureAwait(false);
            EntityFootprint staged = WithRequestPrimary(http, bypass.Footprint);
            EntityFootprintStaging.Stage(http, staged);
            SetData(http, DataCacheResult.Bypass);
            RecordFusionAndAdmin(http, opts.Domain, opts.Domain, "bypass", null, null);
            return bypass.IsMiss ? default : bypass.Value;
        }

        if (opts.FusionCacheRespectNoStore
            && HttpHelper.ContainsCacheDirective(http.Request.Headers.CacheControl, "no-store"))
        {
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("FusionCache skipped due to Cache-Control: no-store");

            FootprintCacheBox<T?> bypass = await factory(cancellationToken).ConfigureAwait(false);
            EntityFootprint staged = WithRequestPrimary(http, bypass.Footprint);
            EntityFootprintStaging.Stage(http, staged);
            SetData(http, DataCacheResult.Bypass);
            RecordFusionAndAdmin(http, opts.Domain, opts.Domain, "bypass", null, null);
            return bypass.IsMiss ? default : bypass.Value;
        }

        string? previousId = null;
        ICacheOrchestratorFeature? feature = http.Features.Get<ICacheOrchestratorFeature>();
        if (feature is not null)
        {
            previousId = feature.ResourceId;
            if (!useEntityKey && previousId is not null)
                feature.ResourceId = null;
        }

        string key;
        try
        {
            key = _keyGenerator.Generate(opts, http);
        }
        finally
        {
            if (!useEntityKey && feature is not null && previousId is not null)
                feature.ResourceId = previousId;
        }

        FusionCacheEntryOptions options = opts.GetFusionEntryOptions();
        IFusionCache fusion = _fusionProvider.GetCache(opts.FusionCacheInstanceName);
        // Early tags (mutable — factory may append before Fusion Set reads them).
        List<string> tags = [CacheTags.Domain(opts.Domain)];
        EntityRef? primary = TryGetRequestPrimary(http);
        if (useEntityKey && primary is { } p)
        {
            tags.Add(CacheTags.Entity(opts.Domain, p.EntityKind, p.ResourceId));
            tags.Add(CacheTags.EntityKind(opts.Domain, p.EntityKind));
        }
        else if (!useEntityKey)
        {
            string? kind = TryGetRequestEntityKind(http);
            if (!string.IsNullOrEmpty(kind))
                tags.Add(CacheTags.EntityKind(opts.Domain, kind));
        }

        bool materialized = false;
        bool factoryFailed = false;
        Stopwatch sw = Stopwatch.StartNew();

        using Activity? activity = CacheOrchestratorActivitySource.Source.StartActivity("cache.fusion.get_or_set");
        activity?.SetTag("domain", opts.Domain);
        if (primary is { } prim)
        {
            activity?.SetTag("entity_kind", prim.EntityKind);
            activity?.SetTag("resource_id", prim.ResourceId);
        }

        FootprintCacheBox<T?> box;
        try
        {
            box = await fusion.GetOrSetAsync<FootprintCacheBox<T?>>(
                    key,
                    async (_, token) =>
                    {
                        materialized = true;
                        try
                        {
                            FootprintCacheBox<T?> produced = await factory(token).ConfigureAwait(false);
                            EntityFootprint full = WithRequestPrimary(http, produced.Footprint);
                            tags.Clear();
                            foreach (string tag in full.ToTags(opts.Domain))
                                tags.Add(tag);

                            return new FootprintCacheBox<T?>
                            {
                                Value = produced.Value,
                                IsMiss = produced.IsMiss,
                                Footprint = full
                            };
                        }
                        catch (OperationCanceledException)
                        {
                            factoryFailed = true;
                            throw;
                        }
                        catch
                        {
                            factoryFailed = true;
                            throw;
                        }
                    },
                    options,
                    tags: tags,
                    token: cancellationToken)
                .ConfigureAwait(false);

            // Ensure Fusion entry has the full tag set (in case tags were snapshotted early).
            if (materialized && !factoryFailed)
            {
                await fusion.SetAsync(
                        key,
                        box,
                        options,
                        tags: box.Footprint.ToTags(opts.Domain),
                        token: cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "canceled");
            throw;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            if (factoryFailed)
            {
                sw.Stop();
                RecordFusionAndAdmin(
                    http,
                    opts.Domain,
                    opts.Domain,
                    "fail",
                    sw.ElapsedMilliseconds,
                    sw.ElapsedTicks);
                if (_logger.IsEnabled(LogLevel.Warning))
                    _logger.LogWarning(ex, "FusionCache ERROR Key={Key}, Error={Error}", key, ex.Message);
            }

            throw;
        }

        EntityFootprintStaging.Stage(http, box.Footprint);

        sw.Stop();
        DataCacheResult dataResult = factoryFailed
            ? DataCacheResult.Stale
            : materialized
                ? DataCacheResult.Miss
                : DataCacheResult.Hit;

        SetData(http, dataResult, sw.ElapsedMilliseconds);
        string resultCode = DataToMetric(dataResult);
        activity?.SetTag("cache.result", resultCode);

        long? resultSizeBytes = materialized && !factoryFailed
            ? FactoryResultSize.TryEstimateBytes(box.Value)
            : null;

        RecordFusionAndAdmin(
            http,
            opts.Domain,
            opts.Domain,
            resultCode,
            sw.ElapsedMilliseconds,
            sw.ElapsedTicks,
            resultSizeBytes);

        if (dataResult == DataCacheResult.Stale)
        {
            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation("FusionCache STALE Key={Key}, Elapsed={ElapsedMs} ms", key, sw.ElapsedMilliseconds);
        }
        else if (_logger.IsEnabled(LogLevel.Debug))
        {
            if (dataResult == DataCacheResult.Miss)
                _logger.LogDebug("FusionCache MISS Key={Key}, Elapsed={ElapsedMs} ms", key, sw.ElapsedMilliseconds);
            else
                _logger.LogDebug("FusionCache HIT Key={Key}, Elapsed={ElapsedMs} ms", key, sw.ElapsedMilliseconds);
        }

        return box.IsMiss ? default : box.Value;
    }

    private async Task<T> GetOrSetCoreAsync<T>(
        HttpContext http,
        string? domain,
        string? entityKind,
        string? resourceId,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(factory);

        DomainCacheOptions? opts = ResolveDomainOptions(http, domain);

        if (opts is null)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(
                    "FusionCache skipped: no domain resolved for {Method} {Path}. " +
                    "Factory runs uncached. Use .CacheOutputWithDomain / [CacheDomain], " +
                    "GetOrSetAsync(http, domain, factory), or EnsureDomainOptions.",
                    http.Request.Method,
                    http.Request.Path.Value);
            }

            using Activity? unresolvedActivity =
                CacheOrchestratorActivitySource.Source.StartActivity("cache.fusion.get_or_set");
            unresolvedActivity?.SetTag("domain", "_");
            unresolvedActivity?.SetTag("cache.result", "unresolved");

            return await InvokeFactoryUncachedAsync(
                    http,
                    DataCacheResult.Unresolved,
                    "unresolved",
                    metricsDomain: "_",
                    adminDomain: null,
                    factory,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        string? normalizedEntityKind = null;
        string? normalizedResourceId = null;
        ICacheOrchestratorFeature? feature = http.Features.Get<ICacheOrchestratorFeature>();
        string? previousKind = feature?.EntityKind;
        string? previousId = feature?.ResourceId;
        bool replacedIdentity = false;
        if (!string.IsNullOrWhiteSpace(entityKind) && !string.IsNullOrWhiteSpace(resourceId))
        {
            normalizedEntityKind = DomainName.NormalizeEntityKind(entityKind);
            normalizedResourceId = DomainName.NormalizeResourceId(resourceId);
            if (!string.IsNullOrEmpty(normalizedEntityKind) && !string.IsNullOrEmpty(normalizedResourceId))
            {
                feature = CacheOrchestratorFeatureAccessor.GetOrCreate(http);
                feature.EntityKind = normalizedEntityKind;
                feature.ResourceId = normalizedResourceId;
                replacedIdentity = true;
            }
            else
            {
                normalizedEntityKind = null;
                normalizedResourceId = null;
            }
        }

        try
        {
            return await GetOrSetWithOptionsAsync(
                    http,
                    opts,
                    normalizedEntityKind,
                    normalizedResourceId,
                    factory,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            if (replacedIdentity && feature is not null)
            {
                feature.EntityKind = previousKind;
                feature.ResourceId = previousId;
            }
        }
    }

    private async Task<T> GetOrSetWithOptionsAsync<T>(
        HttpContext http,
        DomainCacheOptions opts,
        string? normalizedEntityKind,
        string? normalizedResourceId,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken)
    {
        if (!opts.FusionCacheEnabled)
        {
            using Activity? offActivity = CacheOrchestratorActivitySource.Source.StartActivity("cache.fusion.get_or_set");
            offActivity?.SetTag("domain", opts.Domain);
            offActivity?.SetTag("cache.result", "off");
            return await InvokeFactoryUncachedAsync(
                    http,
                    DataCacheResult.Off,
                    "off",
                    opts.Domain,
                    opts.Domain,
                    factory,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (opts.FusionRespectAuthBypass && DomainAuthEvaluator.ShouldBypassForAuth(http, opts))
        {
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("FusionCache skipped due to auth bypass (FusionRespectAuthBypass)");

            using Activity? authBypassActivity = CacheOrchestratorActivitySource.Source.StartActivity("cache.fusion.get_or_set");
            authBypassActivity?.SetTag("domain", opts.Domain);
            authBypassActivity?.SetTag("cache.result", "bypass");
            return await InvokeFactoryUncachedAsync(
                    http,
                    DataCacheResult.Bypass,
                    "bypass",
                    opts.Domain,
                    opts.Domain,
                    factory,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (opts.FusionCacheRespectNoStore
            && HttpHelper.ContainsCacheDirective(http.Request.Headers.CacheControl, "no-store"))
        {
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("FusionCache skipped due to Cache-Control: no-store");

            using Activity? bypassActivity = CacheOrchestratorActivitySource.Source.StartActivity("cache.fusion.get_or_set");
            bypassActivity?.SetTag("domain", opts.Domain);
            bypassActivity?.SetTag("cache.result", "bypass");
            return await InvokeFactoryUncachedAsync(
                    http,
                    DataCacheResult.Bypass,
                    "bypass",
                    opts.Domain,
                    opts.Domain,
                    factory,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        // URL-shaped GetOrSet must not pick up request entity identity stamped by OC / SetEntityIdentity.
        bool clearIdentityForKey = string.IsNullOrEmpty(normalizedEntityKind) || string.IsNullOrEmpty(normalizedResourceId);
        ICacheOrchestratorFeature? feature = http.Features.Get<ICacheOrchestratorFeature>();
        string? kindForKey = feature?.EntityKind;
        string? idForKey = feature?.ResourceId;
        
        if (clearIdentityForKey && feature is not null)
        {
            feature.EntityKind = null;
            feature.ResourceId = null;
        }

        string key;
        try
        {
            key = _keyGenerator.Generate(opts, http);
        }
        finally
        {
            if (clearIdentityForKey && feature is not null)
            {
                feature.EntityKind = kindForKey;
                feature.ResourceId = idForKey;
            }
        }

        FusionCacheEntryOptions options = opts.GetFusionEntryOptions();
        IFusionCache fusion = _fusionProvider.GetCache(opts.FusionCacheInstanceName);
        string[] tags = BuildTags(opts.Domain, normalizedEntityKind, normalizedResourceId);

        bool materialized = false;
        bool factoryFailed = false;
        Stopwatch sw = Stopwatch.StartNew();

        using Activity? activity = CacheOrchestratorActivitySource.Source.StartActivity("cache.fusion.get_or_set");
        activity?.SetTag("domain", opts.Domain);
        if (normalizedEntityKind is not null)
            activity?.SetTag("entity_kind", normalizedEntityKind);
        if (normalizedResourceId is not null)
            activity?.SetTag("resource_id", normalizedResourceId);

        T result;
        try
        {
            result = await fusion.GetOrSetAsync<T>(
                key,
                async (_, token) =>
                {
                    materialized = true;
                    try
                    {
                        return await factory(token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        factoryFailed = true;
                        if (_logger.IsEnabled(LogLevel.Debug))
                            _logger.LogDebug("Factory canceled for Key={Key}", key);
                        throw;
                    }
                    catch
                    {
                        factoryFailed = true;
                        throw;
                    }
                },
                options,
                tags: tags,
                token: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "canceled");
            if (factoryFailed && _logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("FusionCache CANCEL Key={Key}", key);
            throw;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            if (factoryFailed)
            {
                sw.Stop();
                RecordFusionAndAdmin(
                    http,
                    opts.Domain,
                    opts.Domain,
                    "fail",
                    durationMs: sw.ElapsedMilliseconds,
                    elapsedTicks: sw.ElapsedTicks,
                    resultSizeBytes: null);
                if (_logger.IsEnabled(LogLevel.Warning))
                    _logger.LogWarning(ex, "FusionCache ERROR Key={Key}, Error={Error}", key, ex.Message);
            }

            throw;
        }

        sw.Stop();
        long elapsed = sw.ElapsedMilliseconds;

        DataCacheResult dataResult = factoryFailed
            ? DataCacheResult.Stale
            : materialized
                ? DataCacheResult.Miss
                : DataCacheResult.Hit;

        SetData(http, dataResult, elapsed);

        string resultCode = DataToMetric(dataResult);
        activity?.SetTag("cache.result", resultCode);

        long? resultSizeBytes = materialized && !factoryFailed
            ? FactoryResultSize.TryEstimateBytes(result)
            : null;

        RecordFusionAndAdmin(
            http,
            opts.Domain,
            opts.Domain,
            resultCode,
            durationMs: elapsed,
            elapsedTicks: sw.ElapsedTicks,
            resultSizeBytes: resultSizeBytes);

        if (dataResult == DataCacheResult.Stale)
        {
            if (_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation("FusionCache STALE Key={Key}, Elapsed={ElapsedMs} ms", key, elapsed);
        }
        else if (_logger.IsEnabled(LogLevel.Debug))
        {
            if (dataResult == DataCacheResult.Miss)
                _logger.LogDebug("FusionCache MISS Key={Key}, Elapsed={ElapsedMs} ms", key, elapsed);
            else
                _logger.LogDebug("FusionCache HIT Key={Key}, Elapsed={ElapsedMs} ms", key, elapsed);
        }

        return result;
    }

    private static EntityFootprint WithRequestPrimary(HttpContext http, EntityFootprint extra)
    {
        EntityRef? primary = TryGetRequestPrimary(http);
        if (primary is null)
            return extra ?? EntityFootprint.Empty;

        return (extra ?? EntityFootprint.Empty).WithPrimary(primary.Value);
    }

    private static EntityRef? TryGetRequestPrimary(HttpContext http)
    {
        string? kind = TryGetRequestEntityKind(http);
        string? id = TryGetRequestResourceId(http);
        if (string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(id))
            return null;
        return new EntityRef(kind, id);
    }

    private static string? TryGetRequestEntityKind(HttpContext http) =>
        http.Features.Get<ICacheOrchestratorFeature>()?.EntityKind is { Length: > 0 } kind ? kind : null;

    private static string? TryGetRequestResourceId(HttpContext http) =>
        http.Features.Get<ICacheOrchestratorFeature>()?.ResourceId is { Length: > 0 } id ? id : null;

    private static void EnsureRequestHasPrimaryIdentity(HttpContext http)
    {
        if (TryGetRequestPrimary(http) is not null)
            return;

        throw new InvalidOperationException(
            "Entity identity is not on the request. Use [CacheDomain(domain, resourceRouteKey, entityKind)] / " +
            "CacheOutputWithDomain(domain, resourceRouteKey, entityKind), or SetEntityIdentity.");
    }

    /// <summary>
    /// 1) Explicit domain argument → EnsureDomainOptions (replaces a different snapshot on the request).
    /// 2) Options already on the request (Output Cache policy usually set them).
    /// 3) Endpoint metadata (DomainOutputCachePolicy / CacheDomainAttribute) → EnsureDomainOptions.
    /// </summary>
    private DomainCacheOptions? ResolveDomainOptions(HttpContext http, string? domain)
    {
        DomainCacheOptions? opts = _domainConfig.GetDomainOptions(http);

        if (!string.IsNullOrWhiteSpace(domain))
        {
            if (opts is not null
                && string.Equals(opts.Domain, DomainName.Normalize(domain), StringComparison.Ordinal))
            {
                return opts;
            }

            return _domainConfig.EnsureDomainOptions(http, domain);
        }

        if (opts is not null)
            return opts;

        string? fromEndpoint = TryResolveDomainFromEndpoint(http);
        if (!string.IsNullOrWhiteSpace(fromEndpoint))
            return _domainConfig.EnsureDomainOptions(http, fromEndpoint);

        return null;
    }

    private static string? TryResolveDomainFromEndpoint(HttpContext http)
    {
        Endpoint? endpoint = http.GetEndpoint();
        if (endpoint is null)
            return null;

        DomainOutputCachePolicy? policy = endpoint.Metadata.OfType<DomainOutputCachePolicy>().LastOrDefault();
        if (policy is not null)
        {
            string resolved = policy.ResolveDomain(http);
            if (!string.IsNullOrWhiteSpace(resolved))
                return resolved;
        }

        CacheDomainAttribute? attr = endpoint.Metadata.OfType<CacheDomainAttribute>().LastOrDefault();
        return attr?.Domain;
    }

    private static void EnsureUsableEntityIdentity(string entityKind, string resourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        if (string.IsNullOrEmpty(DomainName.NormalizeEntityKind(entityKind)))
            throw new ArgumentException("Entity kind must contain usable characters after normalization.", nameof(entityKind));
        if (string.IsNullOrEmpty(DomainName.NormalizeResourceId(resourceId)))
            throw new ArgumentException("Resource id must contain usable characters after normalization.", nameof(resourceId));
    }

    private static string[] BuildTags(string domain, string? normalizedEntityKind, string? normalizedResourceId)
    {
        if (string.IsNullOrEmpty(normalizedEntityKind) || string.IsNullOrEmpty(normalizedResourceId))
            return [CacheTags.Domain(domain)];

        return
        [
            CacheTags.Domain(domain),
            CacheTags.Entity(domain, normalizedEntityKind, normalizedResourceId),
            CacheTags.EntityKind(domain, normalizedEntityKind)
        ];
    }

    private async Task<T> InvokeFactoryUncachedAsync<T>(
        HttpContext http,
        DataCacheResult dataResult,
        string metricResult,
        string metricsDomain,
        string? adminDomain,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken)
    {
        Stopwatch sw = Stopwatch.StartNew();
        T result;
        try
        {
            result = await factory(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            sw.Stop();
            RecordFusionAndAdmin(
                http,
                metricsDomain,
                adminDomain,
                "fail",
                sw.ElapsedMilliseconds,
                sw.ElapsedTicks);
            throw;
        }

        sw.Stop();
        SetData(http, dataResult, sw.ElapsedMilliseconds);
        RecordFusionAndAdmin(
            http,
            metricsDomain,
            adminDomain,
            metricResult,
            sw.ElapsedMilliseconds,
            sw.ElapsedTicks,
            FactoryResultSize.TryEstimateBytes(result));
        return result;
    }

    private static void SetData(HttpContext http, DataCacheResult data, long? ms = null)
    {
        ICacheOrchestratorFeature feature = CacheOrchestratorFeatureAccessor.GetOrCreate(http);

        if (feature.Disposition is { } existing)
        {
            existing.Data = data;
            existing.ElapsedMs = ms;
        }
        else
        {
            feature.Disposition = new CacheDisposition { Data = data, ElapsedMs = ms };
        }
    }

    private static string DataToMetric(DataCacheResult d) => d switch
    {
        DataCacheResult.Hit => "hit",
        DataCacheResult.Stale => "stale",
        DataCacheResult.Bypass => "bypass",
        DataCacheResult.Off => "off",
        DataCacheResult.Unresolved => "unresolved",
        DataCacheResult.Miss => "miss",
        _ => "miss"
    };

    private void RecordFusionAndAdmin(
        HttpContext http,
        string metricsDomain,
        string? adminDomain,
        string result,
        double? durationMs,
        long? elapsedTicks,
        long? resultSizeBytes = null)
    {
        CacheOrchestratorMetrics.ResolveEndpointKeys(
            http,
            forAdminStats: _adminStats.IsEnabled,
            out string? endpointKey,
            out string? metricsRoute);
        CacheOrchestratorMetrics.RecordFusion(
            metricsDomain, result, durationMs, metricsRoute, resultSizeBytes);

        if (!_adminStats.IsEnabled)
            return;

        long? ticks = _adminStats.TrackLatency ? elapsedTicks : null;
        long? size = _adminStats.TrackResultSize ? resultSizeBytes : null;
        _adminStats.RecordFusion(endpointKey, adminDomain, result, ticks, size);
    }
}