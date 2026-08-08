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

    /// <summary>
    /// Initializes a new instance of the <see cref="DomainFusionCacheService"/> class.
    /// </summary>
    public DomainFusionCacheService(
        IFusionCacheProvider fusionProvider,
        IDomainCacheOptionsProvider domainConfig,
        IDomainKeyGenerator keyGenerator,
        ILogger<DomainFusionCacheService> logger)
    {
        ArgumentNullException.ThrowIfNull(fusionProvider);
        ArgumentNullException.ThrowIfNull(domainConfig);
        ArgumentNullException.ThrowIfNull(keyGenerator);
        ArgumentNullException.ThrowIfNull(logger);

        _fusionProvider = fusionProvider;
        _domainConfig = domainConfig;
        _keyGenerator = keyGenerator;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<T> GetOrSetAsync<T>(
        HttpContext http,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken = default)
        => GetOrSetCoreAsync(http, domain: null, resourceId: null, factory, cancellationToken);

    /// <inheritdoc />
    public Task<T> GetOrSetAsync<T>(
        HttpContext http,
        string domain,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        return GetOrSetCoreAsync(http, domain, resourceId: null, factory, cancellationToken);
    }

    /// <inheritdoc />
    public Task<T> GetOrSetAsync<T>(
        HttpContext http,
        string domain,
        string resourceId,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        return GetOrSetCoreAsync(http, domain, resourceId, factory, cancellationToken);
    }

    private async Task<T> GetOrSetCoreAsync<T>(
        HttpContext http,
        string? domain,
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

            SetData(http, DataCacheResult.Unresolved);
            CacheOrchestratorMetrics.RecordFusion(domain: "_", result: "unresolved");

            using Activity? unresolvedActivity =
                CacheOrchestratorActivitySource.Source.StartActivity("cache.fusion.get_or_set");
            unresolvedActivity?.SetTag("domain", "_");
            unresolvedActivity?.SetTag("cache.result", "unresolved");

            return await factory(cancellationToken).ConfigureAwait(false);
        }

        string? normalizedResourceId = null;
        if (!string.IsNullOrWhiteSpace(resourceId))
        {
            normalizedResourceId = DomainName.NormalizeResourceId(resourceId);
            if (!string.IsNullOrEmpty(normalizedResourceId))
                http.Items[CacheOrchestratorKeys.ResourceIdKey] = normalizedResourceId;
        }

        if (!opts.FusionCacheEnabled)
        {
            SetData(http, DataCacheResult.Off);
            return await factory(cancellationToken).ConfigureAwait(false);
        }

        // Respect Cache-Control: no-store (avoid Header.ToString() allocation on the hot path)
        if (opts.FusionCacheRespectNoStore
            && HttpHelper.ContainsCacheDirective(http.Request.Headers.CacheControl, "no-store"))
        {
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("FusionCache skipped due to Cache-Control: no-store");

            SetData(http, DataCacheResult.Bypass);
            CacheOrchestratorMetrics.RecordFusion(opts.Domain, "bypass");

            using Activity? bypassActivity = CacheOrchestratorActivitySource.Source.StartActivity("cache.fusion.get_or_set");
            bypassActivity?.SetTag("domain", opts.Domain);
            bypassActivity?.SetTag("cache.result", "bypass");
            return await factory(cancellationToken).ConfigureAwait(false);
        }

        string key = _keyGenerator.Generate(opts, http);
        // Shared per domain snapshot — not allocated on every GetOrSet.
        FusionCacheEntryOptions options = opts.GetFusionEntryOptions();

        // Resolve the named FusionCache instance for this domain.
        IFusionCache fusion = _fusionProvider.GetCache(opts.FusionCacheInstanceName);

        string[] tags = BuildTags(opts.Domain, normalizedResourceId);

        bool materialized = false;
        bool factoryFailed = false;
        Stopwatch sw = Stopwatch.StartNew();

        using Activity? activity = CacheOrchestratorActivitySource.Source.StartActivity("cache.fusion.get_or_set");
        activity?.SetTag("domain", opts.Domain);
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
            if (factoryFailed && _logger.IsEnabled(LogLevel.Warning))
                _logger.LogWarning(ex, "FusionCache ERROR Key={Key}, Error={Error}", key, ex.Message);
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
        CacheOrchestratorMetrics.RecordFusion(opts.Domain, resultCode, elapsed);

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

    /// <summary>
    /// 1) Options already on the request (Output Cache policy usually set them).
    /// 2) Explicit domain argument → EnsureDomainOptions.
    /// 3) Endpoint metadata (DomainOutputCachePolicy / CacheDomainAttribute) → EnsureDomainOptions.
    /// </summary>
    private DomainCacheOptions? ResolveDomainOptions(HttpContext http, string? domain)
    {
        DomainCacheOptions? opts = _domainConfig.GetDomainOptions(http);
        if (opts is not null)
            return opts;

        if (!string.IsNullOrWhiteSpace(domain))
            return _domainConfig.EnsureDomainOptions(http, domain);

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

    private static string[] BuildTags(string domain, string? normalizedResourceId)
    {
        if (string.IsNullOrEmpty(normalizedResourceId))
            return [CacheTags.Domain(domain)];

        return
        [
            CacheTags.Domain(domain),
            CacheTags.Entity(domain, normalizedResourceId)
        ];
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
}