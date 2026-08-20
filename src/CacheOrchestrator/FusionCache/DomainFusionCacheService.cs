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
            // No domain on the request and none on endpoint metadata — factory runs uncached.
            // This is intentional for some callers, but easy to misconfigure; always surface it.
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
        bool hadPreviousKind = http.Items.TryGetValue(CacheOrchestratorKeys.EntityKindKey, out object? previousKind);
        bool hadPreviousId = http.Items.TryGetValue(CacheOrchestratorKeys.ResourceIdKey, out object? previousId);
        bool replacedIdentity = false;
        if (!string.IsNullOrWhiteSpace(entityKind) && !string.IsNullOrWhiteSpace(resourceId))
        {
            normalizedEntityKind = DomainName.NormalizeEntityKind(entityKind);
            normalizedResourceId = DomainName.NormalizeResourceId(resourceId);
            if (!string.IsNullOrEmpty(normalizedEntityKind) && !string.IsNullOrEmpty(normalizedResourceId))
            {
                http.Items[CacheOrchestratorKeys.EntityKindKey] = normalizedEntityKind;
                http.Items[CacheOrchestratorKeys.ResourceIdKey] = normalizedResourceId;
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
            if (replacedIdentity)
            {
                RestoreItem(http, CacheOrchestratorKeys.EntityKindKey, hadPreviousKind, previousKind);
                RestoreItem(http, CacheOrchestratorKeys.ResourceIdKey, hadPreviousId, previousId);
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

        // Parity with Output Cache auth bypass (default true). Set FusionRespectAuthBypass=false for 2.1-like Fusion-under-Authorization.
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

        // Respect Cache-Control: no-store (avoid Header.ToString() allocation on the hot path)
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

        string key = _keyGenerator.Generate(opts, http);
        // Shared per domain snapshot — not allocated on every GetOrSet.
        FusionCacheEntryOptions options = opts.GetFusionEntryOptions();

        // Resolve the named FusionCache instance for this domain.
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
                async (ctx, token) =>
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
                // Hard factory failure (no fail-safe stale returned) — OTEL result=fail for analytics.
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

        // Size only for successful factory materialization (miss), when cheap to measure.
        // OTel records when non-null; Admin stores only if TrackResultSize.
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

    private static void RestoreItem(HttpContext http, object key, bool hadPrevious, object? previous)
    {
        if (hadPrevious)
            http.Items[key] = previous;
        else
            http.Items.Remove(key);
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

        // Prefer output-cache policy metadata (fixed, func, or template).
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
        if (http.Items.TryGetValue(CacheOrchestratorKeys.DispositionKey, out object? obj)
            && obj is CacheDisposition existing)
        {
            existing.Data = data;
            existing.ElapsedMs = ms;
            return;
        }

        http.Items[CacheOrchestratorKeys.DispositionKey] = new CacheDisposition
        {
            Data = data,
            ElapsedMs = ms
        };
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

    /// <summary>
    /// Single endpoint-key resolution for meter + Local Admin Fusion counters.
    /// </summary>
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