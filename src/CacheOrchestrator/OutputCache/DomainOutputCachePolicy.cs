using CacheOrchestrator.Admin;
using CacheOrchestrator.Configuration;
using CacheOrchestrator.Diagnostics;
using CacheOrchestrator.FusionCache;
using CacheOrchestrator.Utilities;
using CacheOrchestrator.Vary;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace CacheOrchestrator.OutputCache;

/// <summary>
/// Output cache policy that resolves per-domain settings, tags entries with <c>domain:{name}</c>
/// (and optional <c>entity:{domain}:{entityKind}:{id}</c>), and applies client <c>Cache-Control</c> / optional
/// diagnostic <c>X-Cache</c> / ETag headers.
/// </summary>
/// <remarks>
/// Instances are created as endpoint metadata (not via DI). The logger and options are resolved from
/// <see cref="HttpContext.RequestServices"/> at request time.
/// Diagnostic headers honour <see cref="CacheOrchestratorOptions.EmitDiagnosticsHeaders"/> (default on).
/// </remarks>
public sealed class DomainOutputCachePolicy : IOutputCachePolicy, IFilterMetadata
{
    private static readonly object LoggerItemsKey = new();
    private readonly Func<HttpContext, string> _domainProvider;

    /// <summary>
    /// Creates a policy bound to a fixed cache domain.
    /// </summary>
    /// <param name="domain">Non-empty domain name (normalized on construction).</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="domain"/> is null, empty, or whitespace.</exception>
    public DomainOutputCachePolicy(string domain)
        : this(domain, resourceRouteKey: null, entityKind: null, requireEntity: false)
    {
    }

    /// <summary>
    /// Creates a policy bound to a fixed cache domain with kind-scoped tagging (lists / collections).
    /// </summary>
    /// <param name="domain">Non-empty domain name (normalized on construction).</param>
    /// <param name="entityKind">Resource type within the domain (e.g. <c>products</c>).</param>
    public DomainOutputCachePolicy(string domain, string entityKind)
        : this(domain, resourceRouteKey: null, entityKind, requireEntity: false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityKind);
        if (EntityKind is null)
            throw new ArgumentException("Entity kind must contain usable characters.", nameof(entityKind));
    }

    /// <summary>
    /// Creates a policy bound to a fixed cache domain with entity tagging from a route value.
    /// </summary>
    /// <param name="domain">Non-empty domain name (normalized on construction).</param>
    /// <param name="resourceRouteKey">Route value name for the resource id (e.g. <c>"id"</c>).</param>
    /// <param name="entityKind">Resource type within the domain (e.g. <c>products</c>).</param>
    public DomainOutputCachePolicy(string domain, string resourceRouteKey, string entityKind)
        : this(domain, resourceRouteKey, entityKind, requireEntity: true)
    {
    }

    private DomainOutputCachePolicy(string domain, string? resourceRouteKey, string? entityKind, bool requireEntity = false)
    {
        if (string.IsNullOrWhiteSpace(domain))
            throw new ArgumentException("Domain must not be null or empty.", nameof(domain));
        if (requireEntity)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(resourceRouteKey);
            ArgumentException.ThrowIfNullOrWhiteSpace(entityKind);
        }

        string fixedDomain = DomainName.Normalize(domain);
        _domainProvider = _ => fixedDomain;
        FixedDomain = fixedDomain;
        ResourceRouteKey = string.IsNullOrWhiteSpace(resourceRouteKey) ? null : resourceRouteKey.Trim();
        EntityKind = NormalizeConfiguredEntityKind(entityKind, requireEntity);
    }

    /// <summary>
    /// Creates a policy that resolves the cache domain per request.
    /// </summary>
    /// <param name="domainResolver">Delegate that returns the domain for the current <see cref="HttpContext"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="domainResolver"/> is null.</exception>
    public DomainOutputCachePolicy(Func<HttpContext, string> domainResolver)
        : this(domainResolver, resourceRouteKey: null, entityKind: null, requireEntity: false)
    {
    }

    /// <summary>
    /// Creates a policy that resolves the cache domain per request, with kind-scoped tagging.
    /// </summary>
    /// <param name="domainResolver">Delegate that returns the domain for the current <see cref="HttpContext"/>.</param>
    /// <param name="entityKind">Resource type within the domain (e.g. <c>products</c>).</param>
    public DomainOutputCachePolicy(Func<HttpContext, string> domainResolver, string entityKind)
        : this(domainResolver, resourceRouteKey: null, entityKind, requireEntity: false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityKind);
        if (EntityKind is null)
            throw new ArgumentException("Entity kind must contain usable characters.", nameof(entityKind));
    }

    /// <summary>
    /// Creates a policy that resolves the cache domain per request, with entity tagging from a route value.
    /// </summary>
    /// <param name="domainResolver">Delegate that returns the domain for the current <see cref="HttpContext"/>.</param>
    /// <param name="resourceRouteKey">Route value name for the resource id (e.g. <c>"id"</c>).</param>
    /// <param name="entityKind">Resource type within the domain (e.g. <c>products</c>).</param>
    public DomainOutputCachePolicy(Func<HttpContext, string> domainResolver, string resourceRouteKey, string entityKind)
        : this(domainResolver, resourceRouteKey, entityKind, requireEntity: true)
    {
    }

    private DomainOutputCachePolicy(
        Func<HttpContext, string> domainResolver,
        string? resourceRouteKey,
        string? entityKind,
        bool requireEntity = false)
    {
        ArgumentNullException.ThrowIfNull(domainResolver);
        if (requireEntity)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(resourceRouteKey);
            ArgumentException.ThrowIfNullOrWhiteSpace(entityKind);
        }

        _domainProvider = http => domainResolver(http) ?? string.Empty;
        FixedDomain = null;
        ResourceRouteKey = string.IsNullOrWhiteSpace(resourceRouteKey) ? null : resourceRouteKey.Trim();
        EntityKind = NormalizeConfiguredEntityKind(entityKind, requireEntity);
    }

    /// <summary>
    /// Route value name used for entity tagging, or <see langword="null"/> when not configured.
    /// </summary>
    public string? ResourceRouteKey { get; }

    /// <summary>
    /// Normalized entity kind used for entity tags, or <see langword="null"/> when not configured.
    /// </summary>
    public string? EntityKind { get; }

    /// <summary>
    /// Normalized domain when this policy was constructed with a constant domain name;
    /// <see langword="null"/> when the domain is resolved per request (func/template).
    /// Used by Local Admin endpoint discovery.
    /// </summary>
    public string? FixedDomain { get; }

    /// <summary>
    /// Resolves the cache domain for the current request (fixed, func, or template-backed).
    /// Used by <see cref="FusionCache.IDomainFusionCache"/> when domain options are not yet on the request.
    /// </summary>
    /// <param name="http">Current HTTP context.</param>
    /// <returns>Domain name, or empty if unresolved.</returns>
    public string ResolveDomain(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        return _domainProvider(http) ?? string.Empty;
    }

    private static string? NormalizeConfiguredEntityKind(string? entityKind, bool requireEntity)
    {
        if (string.IsNullOrWhiteSpace(entityKind))
            return null;

        string normalized = DomainName.NormalizeEntityKind(entityKind);
        if (!string.IsNullOrEmpty(normalized))
            return normalized;

        if (requireEntity)
            throw new ArgumentException("Entity kind must contain usable characters.", nameof(entityKind));

        return null;
    }

    private static ILogger<DomainOutputCachePolicy> GetLogger(HttpContext http)
    {
        if (http.Items.TryGetValue(LoggerItemsKey, out object? existing)
            && existing is ILogger<DomainOutputCachePolicy> cached)
        {
            return cached;
        }

        ILogger<DomainOutputCachePolicy> logger =
            http.RequestServices.GetService<ILogger<DomainOutputCachePolicy>>()
            ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DomainOutputCachePolicy>.Instance;
        http.Items[LoggerItemsKey] = logger;
        return logger;
    }

    /// <summary>
    /// Decides whether the request is cacheable and configures vary rules, tags, and TTL for the domain.
    /// </summary>
    /// <param name="context">The output cache context for the current request.</param>
    /// <param name="cancellationToken">Cancellation token (unused; interface contract).</param>
    /// <returns>A completed <see cref="ValueTask"/>.</returns>
    public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        HttpContext http = context.HttpContext;

        string domain = _domainProvider(http);
        if (string.IsNullOrWhiteSpace(domain))
        {
            // Dynamic/template domain unresolved: do not leave the ASP.NET base policy enabled.
            context.EnableOutputCaching = false;
            return ValueTask.CompletedTask;
        }

        DomainCacheOptions opts = http.RequestServices
            .GetRequiredService<IDomainCacheOptionsProvider>()
            .EnsureDomainOptions(http, domain);

        context.EnableOutputCaching = false;

        // Cache only GET/HEAD
        if (!HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method))
            return ValueTask.CompletedTask;

        // Always respect request Cache-Control: no-store (HTTP semantics; token match, not substring).
        if (HttpHelper.ContainsCacheDirective(http.Request.Headers.CacheControl, "no-store"))
        {
            HttpHelper.ApplyNoCache(http.Response);
            RegisterResponseHeaders(http, opts, OutputCacheResult.Bypass, forceClient: ClientCacheClass.NoStore);
            return ValueTask.CompletedTask;
        }

        // Default: no shared caching for auth traffic (safe). Opt out per domain with AuthBypassMode=Never.
        if (DomainAuthEvaluator.ShouldBypassForAuth(http, opts))
        {
            HttpHelper.ApplyNoCache(http.Response);
            RegisterResponseHeaders(http, opts, OutputCacheResult.Bypass, forceClient: ClientCacheClass.Blocked);
            return ValueTask.CompletedTask;
        }

        string? entityKind = TryResolveEntityKind(http);
        string? resourceId = TryResolveResourceId(http);
        ApplyETag(http, opts, entityKind, resourceId);

        if (!opts.OutputCacheEnabled)
        {
            RegisterResponseHeaders(http, opts, OutputCacheResult.Off);
            return ValueTask.CompletedTask;
        }

        if (opts.EncodingNormalizationList != null)
            HttpHelper.NormalizeAcceptEncoding(http, opts.EncodingNormalizationList);
        if (opts.AcceptNormalizationList != null)
            HttpHelper.NormalizeAccept(http, opts.AcceptNormalizationList);
        if (opts.AcceptLanguageNormalizationList != null)
            HttpHelper.NormalizeAcceptLanguage(http, opts.AcceptLanguageNormalizationList);

        context.EnableOutputCaching = true;
        context.AllowCacheLookup = true;
        context.AllowCacheStorage = true;
        context.AllowLocking = true;
        context.ResponseExpirationTimeSpan = opts.OutputTtl;

        CacheVaryMaterializer materializer =
            http.RequestServices.GetService<CacheVaryMaterializer>() ?? new CacheVaryMaterializer();
        CacheVaryMaterial vary = materializer.Build(http, opts, CacheVarySurface.OutputCache);

        context.CacheVaryByRules.VaryByHost = opts.OutputCacheVaryByHost;
        context.CacheVaryByRules.QueryKeys = CacheVaryMaterializer.CollectQueryKeysForOutputCache(http.Request.Query, opts);
        context.CacheVaryByRules.CacheKeyPrefix = opts.OutputCacheNamespace;
        context.CacheVaryByRules.VaryByValues["data-version"] = opts.VersionHex;

        foreach ((string key, string value) in vary.Values)
            context.CacheVaryByRules.VaryByValues[key] = value;

        if (vary.HeaderNames.Count > 0)
        {
            StringValues currentHeaders = context.CacheVaryByRules.HeaderNames;
            for (int i = 0; i < vary.HeaderNames.Count; i++)
            {
                string headerName = vary.HeaderNames[i];
                if (!currentHeaders.Contains(headerName))
                    currentHeaders = StringValues.Concat(currentHeaders, headerName);
            }

            context.CacheVaryByRules.HeaderNames = currentHeaders;
        }

        if (opts.EmitResponseVary && vary.ResponseVaryHeaderNames.Count > 0)
        {
            // Defer writing response Vary until the response starts so we do not advertise secrets.
            http.Response.OnStarting(static state =>
            {
                (HttpContext ctx, IReadOnlyList<string> names) = ((HttpContext, IReadOnlyList<string>))state!;
                AppendResponseVary(ctx.Response, names);
                return Task.CompletedTask;
            }, (http, vary.ResponseVaryHeaderNames));
        }

        context.Tags.Add(CacheTags.Domain(opts.Domain));
        if (!string.IsNullOrEmpty(entityKind) && !string.IsNullOrEmpty(resourceId))
        {
            context.Tags.Add(CacheTags.Entity(opts.Domain, entityKind, resourceId));
            context.Tags.Add(CacheTags.EntityKind(opts.Domain, entityKind));
        }
        else if (!string.IsNullOrEmpty(entityKind))
        {
            // Kind-scoped list / collection endpoints: no single resource id yet.
            context.Tags.Add(CacheTags.EntityKind(opts.Domain, entityKind));
        }

        RegisterResponseHeaders(http, opts, OutputCacheResult.Miss);
        return ValueTask.CompletedTask;
    }

    private static void AppendResponseVary(HttpResponse response, IReadOnlyList<string> headerNames)
    {
        StringValues existing = response.Headers.Vary;
        for (int i = 0; i < headerNames.Count; i++)
        {
            string name = headerNames[i];
            if (existing.Count > 0 && existing.Contains(name))
                continue;
            existing = existing.Count == 0 ? name : StringValues.Concat(existing, name);
        }

        response.Headers.Vary = existing;
    }

    private string? TryResolveEntityKind(HttpContext http)
    {
        if (http.Items.TryGetValue(CacheOrchestratorKeys.EntityKindKey, out object? fromItems)
            && fromItems is string itemKind
            && itemKind.Length > 0)
        {
            return itemKind;
        }

        if (EntityKind is null)
            return null;

        http.Items[CacheOrchestratorKeys.EntityKindKey] = EntityKind;
        return EntityKind;
    }

    private string? TryResolveResourceId(HttpContext http)
    {
        if (http.Items.TryGetValue(CacheOrchestratorKeys.ResourceIdKey, out object? fromItems)
            && fromItems is string itemId
            && itemId.Length > 0)
        {
            return itemId;
        }

        if (ResourceRouteKey is null)
            return null;

        if (!http.Request.RouteValues.TryGetValue(ResourceRouteKey, out object? raw) || raw is null)
            return null;

        string normalized = DomainName.NormalizeResourceId(raw.ToString());
        if (string.IsNullOrEmpty(normalized))
            return null;

        http.Items[CacheOrchestratorKeys.ResourceIdKey] = normalized;
        return normalized;
    }

    private static void ApplyETag(HttpContext http, DomainCacheOptions opts, string? entityKind, string? resourceId)
    {
        switch (opts.ETagMode)
        {
            case ETagMode.None:
                http.Response.Headers.Remove(HeaderNames.ETag);
                break;

            case ETagMode.Resource:
                string resourceKey = resourceId is not null && entityKind is not null
                    ? entityKind + ":" + resourceId
                    : resourceId ?? BuildPathResourceKey(http);
                http.Response.Headers.ETag = CacheETagFactory.FromVersionAndResource(opts.VersionHex, resourceKey);
                break;

            case ETagMode.Version:
            default:
                http.Response.Headers.ETag = opts.ETag;
                break;
        }
    }

    private static string BuildPathResourceKey(HttpContext http)
    {
        // Stable per-URL identity when no explicit resource id is available.
        string path = http.Request.Path.Value ?? "/";
        string query = http.Request.QueryString.Value ?? string.Empty;
        return path + query;
    }

    /// <summary>
    /// Called when a response is served from the output cache (HIT). Records disposition and activity tags.
    /// </summary>
    /// <param name="context">The output cache context for the current request.</param>
    /// <param name="cancellationToken">Cancellation token (unused; interface contract).</param>
    /// <returns>A completed <see cref="ValueTask"/>.</returns>
    public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        HttpContext http = context.HttpContext;
        ILogger<DomainOutputCachePolicy> logger = GetLogger(http);
        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("OutputCache HIT: [{Method}] {Path}", http.Request.Method, http.Request.Path);

        http.Items[CacheOrchestratorKeys.DispositionKey] = new CacheDisposition
        {
            Output = OutputCacheResult.Hit
        };

        using Activity? activity = CacheOrchestratorActivitySource.Source.StartActivity("cache.output.hit");
        if (activity is not null)
        {
            if (http.Items.TryGetValue(CacheOrchestratorKeys.DomainOptionsKey, out object? obj)
                && obj is DomainCacheOptions opts)
            {
                activity.SetTag("domain", opts.Domain);
            }

            activity.SetTag("cache.result", "hit");
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Called before storing a generated response. Disables storage for non-cacheable status codes or sensitive headers.
    /// </summary>
    /// <param name="context">The output cache context for the current request.</param>
    /// <param name="cancellationToken">Cancellation token (unused; interface contract).</param>
    /// <returns>A completed <see cref="ValueTask"/>.</returns>
    public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        HttpContext http = context.HttpContext;
        if (http.Items.TryGetValue(CacheOrchestratorKeys.DomainOptionsKey, out object? obj) && obj is DomainCacheOptions opts)
        {
            if (!IsCacheableStatusCode(http.Response.StatusCode, opts.CacheableStatusCodes)
                || http.Response.Headers.ContainsKey(HeaderNames.SetCookie)
                || http.Response.Headers.ContainsKey(HeaderNames.Authorization))
            {
                context.AllowCacheStorage = false;
            }
            else
            {
                MergePendingFootprintTags(context, opts.Domain);
            }
        }

        return ValueTask.CompletedTask;
    }

    private static void MergePendingFootprintTags(OutputCacheContext context, string domain)
    {
        HttpContext http = context.HttpContext;
        if (!http.Items.TryGetValue(CacheOrchestratorKeys.PendingEntityFootprintKey, out object? raw)
            || raw is not EntityFootprint footprint
            || ReferenceEquals(footprint, EntityFootprint.Empty))
        {
            return;
        }

        IReadOnlyList<string> tags = footprint.ToTags(domain);
        // Skip domain tag (already added in CacheRequestAsync); add the rest with dedupe.
        for (int i = 0; i < tags.Count; i++)
        {
            string tag = tags[i];
            if (tag.StartsWith(CacheTags.DomainPrefix, StringComparison.Ordinal))
                continue;
            if (!context.Tags.Contains(tag))
                context.Tags.Add(tag);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsCacheableStatusCode(int statusCode, int[] allowed)
    {
        if (allowed.Length == 1)
            return allowed[0] == statusCode;

        for (int i = 0; i < allowed.Length; i++)
        {
            if (allowed[i] == statusCode)
                return true;
        }

        return false;
    }

    private static void RegisterResponseHeaders(HttpContext http, DomainCacheOptions opts, OutputCacheResult defaultOutput, ClientCacheClass? forceClient = null) =>
        http.Response.OnStarting(ApplyHeadersAsync, (http, opts, defaultOutput, forceClient));

    private static Task ApplyHeadersAsync(object state)
    {
        (HttpContext? httpContext, DomainCacheOptions? config, OutputCacheResult defOutput, ClientCacheClass? forcedClient) =
            ((HttpContext, DomainCacheOptions, OutputCacheResult, ClientCacheClass?))state;

        HttpResponse response = httpContext.Response;
        int sc = response.StatusCode;

        CacheDisposition disp;
        if (httpContext.Items.TryGetValue(CacheOrchestratorKeys.DispositionKey, out object? raw)
            && raw is CacheDisposition existing)
        {
            disp = existing;
            disp.Output ??= defOutput;
        }
        else
        {
            disp = new CacheDisposition { Output = defOutput };
            httpContext.Items[CacheOrchestratorKeys.DispositionKey] = disp;
        }

        OutputCacheResult output = disp.Output ?? defOutput;

        string ocMetric = output switch
        {
            OutputCacheResult.Hit => "hit",
            OutputCacheResult.Bypass => "bypass",
            OutputCacheResult.Off => "off",
            OutputCacheResult.Miss => "miss",
            _ => "miss"
        };
        IAdminStatsCollector? adminStats = httpContext.RequestServices.GetService<IAdminStatsCollector>();
        bool adminOn = adminStats is { IsEnabled: true };
        CacheOrchestratorMetrics.ResolveEndpointKeys(
            httpContext,
            forAdminStats: adminOn,
            out string? endpointKey,
            out string? metricsRoute);
        CacheOrchestratorMetrics.RecordOutput(config.Domain, ocMetric, metricsRoute);

        if (adminOn)
        {
            adminStats!.RecordOutput(
                endpointKey,
                config.Domain,
                ocMetric);
        }

        ClientCacheClass client;
        ClientCacheSchedulePhase phase = ClientCacheSchedulePhase.NotApplicable;

        if (response.Headers.ContainsKey(HeaderNames.SetCookie) ||
            response.Headers.ContainsKey(HeaderNames.Authorization))
        {
            HttpHelper.ApplyNoCache(response);
            client = ClientCacheClass.Blocked;
        }
        else if (forcedClient is ClientCacheClass.NoStore or ClientCacheClass.Blocked)
        {
            // Auth / no-store request paths pass forceClient so client headers stay non-cacheable
            // even when the response status would otherwise allow a public/private max-age.
            HttpHelper.ApplyNoCache(response);
            client = forcedClient.Value;
        }
        else if (IsCacheableStatusCode(sc, config.CacheableStatusCodes) ||
                 sc == StatusCodes.Status304NotModified)
        {
            TimeProvider timeProvider = httpContext.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System;
            // Cookie/session identity + Public is unsafe for shared browser/CDN caches → force Private.
            // API-key-only traffic (Authorization without IsAuthenticated) may still use Public.
            ClientCacheability? cacheabilityOverride = null;
            if (config.ClientForcePrivateWhenAuthenticated
                && httpContext.User?.Identity?.IsAuthenticated == true
                && config.ClientCacheability == ClientCacheability.Public)
            {
                cacheabilityOverride = ClientCacheability.Private;
            }

            ClientCacheHeaderGenerator.Result built = ClientCacheHeaderGenerator.Build(
                config,
                timeProvider.GetUtcNow(),
                cacheabilityOverride);
            response.Headers.CacheControl = built.Header;
            phase = built.Phase;
            ClientCacheability effectiveCacheability = cacheabilityOverride ?? config.ClientCacheability;
            client = effectiveCacheability switch
            {
                ClientCacheability.Private => ClientCacheClass.Private,
                ClientCacheability.NoStore => ClientCacheClass.NoStore,
                ClientCacheability.Public => ClientCacheClass.Public,
                _ => ClientCacheClass.Public
            };
        }
        else
        {
            HttpHelper.ApplyNoCache(response);
            client = ClientCacheClass.NoStore;
        }

        string phaseWire = XCacheHeaderFormatter.PhaseToString(phase);
        CacheOrchestratorMetrics.RecordClientSchedule(config.Domain, phaseWire);

        // Metrics always; client-visible diagnostic headers are optional (Cache:EmitDiagnosticsHeaders).
        if (ShouldEmitDiagnosticsHeaders(httpContext))
        {
            response.Headers["X-Cache"] = XCacheHeaderFormatter.Format(
                config.Domain,
                client,
                output,
                data: output == OutputCacheResult.Hit ? null : disp.Data,
                ms: output == OutputCacheResult.Hit ? null : disp.ElapsedMs,
                version: config.Version,
                phase: phase);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Reads <see cref="CacheOrchestratorOptions.EmitDiagnosticsHeaders"/> from DI when available.
    /// Defaults to <see langword="true"/> if options are not registered (unit tests / edge hosts).
    /// </summary>
    private static bool ShouldEmitDiagnosticsHeaders(HttpContext httpContext)
    {
        IOptionsMonitor<CacheOrchestratorOptions>? monitor =
            httpContext.RequestServices.GetService<IOptionsMonitor<CacheOrchestratorOptions>>();
        return monitor?.CurrentValue.EmitDiagnosticsHeaders ?? true;
    }

}