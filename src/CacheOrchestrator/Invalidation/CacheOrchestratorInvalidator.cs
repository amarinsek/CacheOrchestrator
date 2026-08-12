using CacheOrchestrator.Admin;
using CacheOrchestrator.Cluster;
using CacheOrchestrator.Configuration;
using CacheOrchestrator.Diagnostics;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using ZiggyCreatures.Caching.Fusion;

namespace CacheOrchestrator.Invalidation;

/// <summary>
/// Default <see cref="ICacheOrchestratorInvalidator"/> that evicts by tag on FusionCache
/// (correct instance for domain/entity, all instances for arbitrary tags) and Output Cache.
/// When a non-null cluster bus is enabled, publishes <see cref="InvalidateCommand"/> after local apply
/// (unless applying under <see cref="ClusterCommandScope"/> remote).
/// </summary>
internal sealed class CacheOrchestratorInvalidator : ICacheOrchestratorInvalidator
{
    private readonly IFusionCacheProvider _fusionProvider;
    private readonly IDomainCacheOptionsProvider _domainOptionsProvider;
    private readonly IOutputCacheStore _outputCacheStore;
    private readonly IOptionsMonitor<CacheOrchestratorOptions> _options;
    private readonly IEnumerable<ICacheInvalidationObserver> _observers;
    private readonly ILogger<CacheOrchestratorInvalidator> _logger;
    private readonly IAdminStatsCollector _adminStats;
    private readonly IClusterCommandBus _clusterBus;
    private readonly IInstanceIdProvider _instanceId;

    /// <summary>
    /// Initializes a new instance of the <see cref="CacheOrchestratorInvalidator"/> class.
    /// </summary>
    public CacheOrchestratorInvalidator(
        IFusionCacheProvider fusionProvider,
        IDomainCacheOptionsProvider domainOptionsProvider,
        IOutputCacheStore outputCacheStore,
        IOptionsMonitor<CacheOrchestratorOptions> options,
        IEnumerable<ICacheInvalidationObserver> observers,
        ILogger<CacheOrchestratorInvalidator> logger,
        IAdminStatsCollector? adminStats = null,
        IClusterCommandBus? clusterBus = null,
        IInstanceIdProvider? instanceId = null)
    {
        ArgumentNullException.ThrowIfNull(fusionProvider);
        ArgumentNullException.ThrowIfNull(domainOptionsProvider);
        ArgumentNullException.ThrowIfNull(outputCacheStore);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(observers);
        ArgumentNullException.ThrowIfNull(logger);

        _fusionProvider = fusionProvider;
        _domainOptionsProvider = domainOptionsProvider;
        _outputCacheStore = outputCacheStore;
        _options = options;
        _observers = observers;
        _logger = logger;
        _adminStats = adminStats ?? NoOpAdminStatsCollector.Instance;
        _clusterBus = clusterBus ?? NullClusterCommandBus.Instance;
        _instanceId = instanceId ?? new FallbackInstanceIdProvider(options);
    }

    /// <summary>Test/DI fallback when <see cref="IInstanceIdProvider"/> is not injected.</summary>
    private sealed class FallbackInstanceIdProvider(IOptionsMonitor<CacheOrchestratorOptions> options) : IInstanceIdProvider
    {
        public string InstanceId => DefaultInstanceIdProvider.Resolve(options.CurrentValue.InstanceId);
    }

    /// <inheritdoc />
    public ValueTask<CacheInvalidationResult> InvalidateDomainAsync(
        string domain,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return ValueTask.FromResult(CacheInvalidationResult.Skipped("Domain was null or whitespace."));

        string normalizedDomain = DomainName.Normalize(domain);
        return InvalidateScopedAsync(
            kind: CacheInvalidationKind.Domain,
            scopeLabel: normalizedDomain,
            tags: [CacheTags.Domain(normalizedDomain)],
            fusionInstanceName: ResolveFusionInstance(normalizedDomain),
            allFusionInstances: false,
            cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<CacheInvalidationResult> InvalidateDomainsAsync(
        IEnumerable<string> domains,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domains);

        List<CacheInvalidationResult> parts = [];
        foreach (string? domain in domains)
        {
            if (string.IsNullOrWhiteSpace(domain))
                continue;

            CacheInvalidationResult part = await InvalidateDomainAsync(domain, cancellationToken)
                .ConfigureAwait(false);
            parts.Add(part);
        }

        if (parts.Count == 0)
            return CacheInvalidationResult.Skipped("No domains provided.");

        // Outer observer for multi-domain aggregate (each domain already notified observers).
        CacheInvalidationResult aggregate = CacheInvalidationResult.Aggregate(parts);
        return aggregate;
    }

    /// <inheritdoc />
    public ValueTask<CacheInvalidationResult> InvalidateEntityAsync(
        string domain,
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(resourceId))
            return ValueTask.FromResult(CacheInvalidationResult.Skipped("Domain or resourceId was null or whitespace."));

        string normalizedDomain = DomainName.Normalize(domain);
        string normalizedResourceId = DomainName.NormalizeResourceId(resourceId);
        if (string.IsNullOrEmpty(normalizedResourceId))
            return ValueTask.FromResult(CacheInvalidationResult.Skipped("ResourceId normalized to empty."));

        string tag = CacheTags.Entity(normalizedDomain, normalizedResourceId);
        return InvalidateScopedAsync(
            kind: CacheInvalidationKind.Entity,
            scopeLabel: $"{normalizedDomain}/{normalizedResourceId}",
            tags: [tag],
            fusionInstanceName: ResolveFusionInstance(normalizedDomain),
            allFusionInstances: false,
            cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<CacheInvalidationResult> InvalidateTagsAsync(
        IEnumerable<string> tags,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tags);

        List<string> list = [];
        foreach (string? tag in tags)
        {
            if (!string.IsNullOrWhiteSpace(tag))
                list.Add(tag.Trim());
        }

        if (list.Count == 0)
            return ValueTask.FromResult(CacheInvalidationResult.Skipped("No tags provided."));

        return InvalidateScopedAsync(
            kind: CacheInvalidationKind.Tags,
            scopeLabel: string.Join(',', list),
            tags: list,
            fusionInstanceName: null,
            allFusionInstances: true,
            cancellationToken);
    }

    private string ResolveFusionInstance(string normalizedDomain)
    {
        DomainCacheOptions domainOpts = _domainOptionsProvider.GetOrCreateDomainOptions(normalizedDomain);
        return domainOpts.FusionCacheInstanceName;
    }

    private async ValueTask<CacheInvalidationResult> InvalidateScopedAsync(
        CacheInvalidationKind kind,
        string scopeLabel,
        IReadOnlyList<string> tags,
        string? fusionInstanceName,
        bool allFusionInstances,
        CancellationToken cancellationToken)
    {
        CacheInvalidationContext observerContext = new(kind, scopeLabel, tags);

        using Activity? activity = CacheOrchestratorActivitySource.Source.StartActivity("cache.invalidate");
        activity?.SetTag("cache.scope", scopeLabel);
        activity?.SetTag("cache.kind", kind.ToString());
        activity?.SetTag("cache.tags", string.Join(',', tags));

        _logger.LogInformation(
            "Invalidating cache scope '{Scope}' kind={Kind} (tags=[{Tags}], all_fc_instances={AllInstances})",
            scopeLabel,
            kind,
            string.Join(", ", tags),
            allFusionInstances);

        await NotifyBeforeAsync(observerContext, cancellationToken).ConfigureAwait(false);

        bool fusionOk = true;
        bool outputOk = true;
        List<string> errors = [];

        IEnumerable<string> instanceNames = allFusionInstances
            ? _options.CurrentValue.FusionCacheInstances.Keys
            : [fusionInstanceName ?? "default"];

        foreach (string instanceName in instanceNames)
        {
            IFusionCache fusion;
            try
            {
                fusion = _fusionProvider.GetCache(instanceName);
            }
            catch (Exception ex)
            {
                fusionOk = false;
                string msg = $"Failed to resolve FusionCache instance '{instanceName}': {ex.Message}";
                errors.Add(msg);
                activity?.AddEvent(new ActivityEvent("fusion.resolve.failed"));
                _logger.LogWarning(ex, "Failed to resolve FusionCache instance '{Instance}'", instanceName);
                continue;
            }

            foreach (string tag in tags)
            {
                try
                {
                    await fusion.RemoveByTagAsync(tag, token: cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    fusionOk = false;
                    string msg = $"Fusion tag '{tag}' on '{instanceName}': {ex.Message}";
                    errors.Add(msg);
                    activity?.AddEvent(new ActivityEvent("fusion.invalidate.failed"));
                    _logger.LogWarning(
                        ex,
                        "Failed to invalidate FusionCache tag '{Tag}' on instance '{Instance}'",
                        tag,
                        instanceName);
                }
            }
        }

        foreach (string tag in tags)
        {
            try
            {
                await _outputCacheStore.EvictByTagAsync(tag, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                outputOk = false;
                string msg = $"OutputCache tag '{tag}': {ex.Message}";
                errors.Add(msg);
                activity?.AddEvent(new ActivityEvent("output.invalidate.failed"));
                _logger.LogWarning(ex, "Failed to invalidate OutputCache tag '{Tag}'", tag);
            }
        }

        activity?.SetTag("cache.fusion.ok", fusionOk);
        activity?.SetTag("cache.output.ok", outputOk);
        if (!fusionOk || !outputOk)
            activity?.SetStatus(ActivityStatusCode.Error, "One or more invalidation targets failed");

        if (fusionOk && outputOk)
        {
            CacheOrchestratorMetrics.RecordInvalidate(scopeLabel);
            RecordAdminInvalidation(kind, scopeLabel);
        }

        CacheInvalidationResult result = new(scopeLabel, tags, fusionOk, outputOk, errors);
        await NotifyAfterAsync(observerContext, result, cancellationToken).ConfigureAwait(false);

        await TryPublishClusterAsync(kind, scopeLabel, tags, cancellationToken).ConfigureAwait(false);

        return result;
    }

    private async ValueTask TryPublishClusterAsync(
        CacheInvalidationKind kind,
        string scopeLabel,
        IReadOnlyList<string> tags,
        CancellationToken cancellationToken)
    {
        if (!_clusterBus.IsEnabled || ClusterCommandScope.IsRemote || tags.Count == 0)
            return;

        CacheOrchestratorOptions opts = _options.CurrentValue;
        string? domain = null;
        string? entityId = null;

        if (kind == CacheInvalidationKind.Domain)
        {
            domain = scopeLabel;
        }
        else if (kind == CacheInvalidationKind.Entity)
        {
            int slash = scopeLabel.IndexOf('/');
            if (slash > 0)
            {
                domain = scopeLabel[..slash];
                entityId = scopeLabel[(slash + 1)..];
            }
        }

        InvalidateCommand command = new()
        {
            CommandId = Guid.NewGuid(),
            OriginInstanceId = _instanceId.InstanceId,
            Namespace = opts.Namespace ?? string.Empty,
            TimestampUtc = DateTimeOffset.UtcNow,
            Kind = kind,
            Scope = scopeLabel,
            Tags = tags is string[] arr ? arr : [.. tags],
            Domain = domain,
            EntityId = entityId
        };

        try
        {
            await _clusterBus.PublishAsync(command, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Local invalidation already finished; peer delivery is best-effort.
            _logger.LogWarning(
                ex,
                "Cluster bus publish failed for scope '{Scope}' kind={Kind} commandId={CommandId}",
                scopeLabel,
                kind,
                command.CommandId);
        }
    }

    private void RecordAdminInvalidation(CacheInvalidationKind kind, string scopeLabel)
    {
        if (!_adminStats.IsEnabled)
            return;

        if (kind == CacheInvalidationKind.Domain)
        {
            _adminStats.RecordInvalidation(scopeLabel);
            return;
        }

        if (kind == CacheInvalidationKind.Entity)
        {
            int slash = scopeLabel.IndexOf('/');
            if (slash > 0)
                _adminStats.RecordInvalidation(scopeLabel[..slash]);
        }

        // Tag-only invalidations are not attributed to a single domain.
    }

    private async ValueTask NotifyBeforeAsync(CacheInvalidationContext context, CancellationToken cancellationToken)
    {
        foreach (ICacheInvalidationObserver observer in _observers)
        {
            try
            {
                await observer.OnBeforeInvalidateAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ICacheInvalidationObserver.OnBeforeInvalidateAsync failed ({Observer})", observer.GetType().Name);
            }
        }
    }

    private async ValueTask NotifyAfterAsync(
        CacheInvalidationContext context,
        CacheInvalidationResult result,
        CancellationToken cancellationToken)
    {
        foreach (ICacheInvalidationObserver observer in _observers)
        {
            try
            {
                await observer.OnAfterInvalidateAsync(context, result, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ICacheInvalidationObserver.OnAfterInvalidateAsync failed ({Observer})", observer.GetType().Name);
            }
        }
    }
}
