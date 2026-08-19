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
    private readonly ClusterCommandFactory? _clusterCommands;

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
        ClusterCommandFactory? clusterCommands = null)
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
        _clusterCommands = clusterCommands;
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

        List<string> requested = [];
        foreach (string? domain in domains)
        {
            if (!string.IsNullOrWhiteSpace(domain))
                requested.Add(domain);
        }

        if (requested.Count == 0)
            return CacheInvalidationResult.Skipped("No domains provided.");

        List<string> tags = new(requested.Count);
        List<string> scopes = new(requested.Count);
        for (int i = 0; i < requested.Count; i++)
        {
            string normalized = DomainName.Normalize(requested[i]);
            scopes.Add(normalized);
            tags.Add(CacheTags.Domain(normalized));
        }

        CacheInvalidationContext batch = new(
            CacheInvalidationKind.Domains,
            string.Join(',', scopes),
            tags);

        await NotifyBeforeAsync(batch, cancellationToken).ConfigureAwait(false);

        List<CacheInvalidationResult> parts = [];
        for (int i = 0; i < requested.Count; i++)
        {
            parts.Add(await InvalidateDomainAsync(requested[i], cancellationToken).ConfigureAwait(false));
        }

        CacheInvalidationResult aggregate = CacheInvalidationResult.Aggregate(parts);
        await NotifyAfterAsync(batch, aggregate, cancellationToken).ConfigureAwait(false);
        return aggregate;
    }

    /// <inheritdoc />
    public ValueTask<CacheInvalidationResult> InvalidateEntityAsync(
        string domain,
        string entityKind,
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(domain)
            || string.IsNullOrWhiteSpace(entityKind)
            || string.IsNullOrWhiteSpace(resourceId))
        {
            return ValueTask.FromResult(
                CacheInvalidationResult.Skipped("Domain, entityKind, or resourceId was null or whitespace."));
        }

        string normalizedDomain = DomainName.Normalize(domain);
        string normalizedKind = DomainName.NormalizeEntityKind(entityKind);
        if (string.IsNullOrEmpty(normalizedKind))
            return ValueTask.FromResult(CacheInvalidationResult.Skipped("EntityKind normalized to empty."));
        string normalizedResourceId = DomainName.NormalizeResourceId(resourceId);
        if (string.IsNullOrEmpty(normalizedResourceId))
            return ValueTask.FromResult(CacheInvalidationResult.Skipped("ResourceId normalized to empty."));

        string tag = CacheTags.Entity(normalizedDomain, normalizedKind, normalizedResourceId);
        return InvalidateScopedAsync(
            kind: CacheInvalidationKind.Entity,
            scopeLabel: $"{normalizedDomain}/{normalizedKind}/{normalizedResourceId}",
            tags: [tag],
            fusionInstanceName: ResolveFusionInstance(normalizedDomain),
            allFusionInstances: false,
            cancellationToken,
            domain: normalizedDomain,
            entityKind: normalizedKind,
            entityId: normalizedResourceId,
            resourceIds: [normalizedResourceId]);
    }

    /// <inheritdoc />
    public ValueTask<CacheInvalidationResult> InvalidateEntitiesAsync(
        string domain,
        string entityKind,
        IEnumerable<string> resourceIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resourceIds);

        if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(entityKind))
        {
            return ValueTask.FromResult(
                CacheInvalidationResult.Skipped("Domain or entityKind was null or whitespace."));
        }

        string normalizedDomain = DomainName.Normalize(domain);
        string normalizedKind = DomainName.NormalizeEntityKind(entityKind);
        if (string.IsNullOrEmpty(normalizedKind))
            return ValueTask.FromResult(CacheInvalidationResult.Skipped("EntityKind normalized to empty."));

        List<string> ids = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (string? raw in resourceIds)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            string id = DomainName.NormalizeResourceId(raw);
            if (id.Length == 0 || !seen.Add(id))
                continue;

            ids.Add(id);
        }

        if (ids.Count == 0)
            return ValueTask.FromResult(CacheInvalidationResult.Skipped("No resourceIds provided."));

        string[] tags = new string[ids.Count];
        for (int i = 0; i < ids.Count; i++)
            tags[i] = CacheTags.Entity(normalizedDomain, normalizedKind, ids[i]);

        return InvalidateScopedAsync(
            kind: CacheInvalidationKind.Entity,
            scopeLabel: $"{normalizedDomain}/{normalizedKind}",
            tags: tags,
            fusionInstanceName: ResolveFusionInstance(normalizedDomain),
            allFusionInstances: false,
            cancellationToken,
            domain: normalizedDomain,
            entityKind: normalizedKind,
            entityId: ids.Count == 1 ? ids[0] : null,
            resourceIds: ids);
    }

    /// <inheritdoc />
    public ValueTask<CacheInvalidationResult> InvalidateEntityKindAsync(
        string domain,
        string entityKind,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(entityKind))
        {
            return ValueTask.FromResult(
                CacheInvalidationResult.Skipped("Domain or entityKind was null or whitespace."));
        }

        string normalizedDomain = DomainName.Normalize(domain);
        string normalizedKind = DomainName.NormalizeEntityKind(entityKind);
        if (string.IsNullOrEmpty(normalizedKind))
            return ValueTask.FromResult(CacheInvalidationResult.Skipped("EntityKind normalized to empty."));
        string tag = CacheTags.EntityKind(normalizedDomain, normalizedKind);
        return InvalidateScopedAsync(
            kind: CacheInvalidationKind.EntityKind,
            scopeLabel: $"{normalizedDomain}/{normalizedKind}",
            tags: [tag],
            fusionInstanceName: ResolveFusionInstance(normalizedDomain),
            allFusionInstances: false,
            cancellationToken,
            domain: normalizedDomain,
            entityKind: normalizedKind);
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
        CancellationToken cancellationToken,
        string? domain = null,
        string? entityKind = null,
        string? entityId = null,
        IReadOnlyList<string>? resourceIds = null)
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
            // Metrics domain tag must stay low-cardinality: never entity id / path.
            // scopeLabel may be "domain/entityKind/id" for entity invalidations.
            string? metricsDomain = ResolveMetricsDomain(kind, scopeLabel, domain);
            if (metricsDomain is not null)
                CacheOrchestratorMetrics.RecordInvalidate(metricsDomain, kind);
            RecordAdminInvalidation(kind, scopeLabel);
        }

        ClusterPublishResult? clusterPublish = await TryPublishClusterAsync(
                kind,
                scopeLabel,
                tags,
                domain,
                entityKind,
                entityId,
                resourceIds,
                cancellationToken)
            .ConfigureAwait(false);

        if (clusterPublish is { AllSucceeded: false })
        {
            foreach (ClusterPeerPublishOutcome failure in clusterPublish.Failures)
            {
                errors.Add($"Cluster peer '{failure.PeerId}': {failure.Error ?? "publish failed"}");
            }
        }

        CacheInvalidationResult result = new(scopeLabel, tags, fusionOk, outputOk, errors, clusterPublish);
        await NotifyAfterAsync(observerContext, result, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private async ValueTask<ClusterPublishResult?> TryPublishClusterAsync(
        CacheInvalidationKind kind,
        string scopeLabel,
        IReadOnlyList<string> tags,
        string? domain,
        string? entityKind,
        string? entityId,
        IReadOnlyList<string>? resourceIds,
        CancellationToken cancellationToken)
    {
        if (!_clusterBus.IsEnabled || ClusterCommandScope.SuppressPublish || tags.Count == 0)
            return null;

        if (_clusterCommands is null)
            return null;

        if (kind == CacheInvalidationKind.Domain && string.IsNullOrEmpty(domain))
            domain = scopeLabel;

        InvalidateCommand command = _clusterCommands.CreateInvalidate(
            kind,
            scopeLabel,
            tags,
            domain,
            entityKind,
            entityId,
            resourceIds);

        try
        {
            ClusterPublishResult published = await _clusterBus.PublishAsync(command, cancellationToken)
                .ConfigureAwait(false);
            CacheOrchestratorMetrics.RecordClusterPublished(nameof(InvalidateCommand));
            return published;
        }
        catch (Exception ex)
        {
            CacheOrchestratorMetrics.RecordClusterPublishFailure("exception");
            _logger.LogWarning(
                ex,
                "Cluster bus publish failed for scope '{Scope}' kind={Kind} commandId={CommandId}",
                scopeLabel,
                kind,
                command.CommandId);
            return new ClusterPublishResult(
            [
                new ClusterPeerPublishOutcome
                {
                    PeerId = "(bus)",
                    Succeeded = false,
                    Error = ex.Message,
                },
            ]);
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

        if (kind is CacheInvalidationKind.Entity or CacheInvalidationKind.EntityKind)
        {
            int slash = scopeLabel.IndexOf('/');
            if (slash > 0)
                _adminStats.RecordInvalidation(scopeLabel[..slash]);
        }

        // Tag-only invalidations are not attributed to a single domain.
    }

    /// <summary>
    /// Domain name for OTel <c>domain</c> label. Never uses resource ids (cardinality).
    /// Tag-only invalidations return null (no domain series).
    /// </summary>
    private static string? ResolveMetricsDomain(CacheInvalidationKind kind, string scopeLabel, string? domain)
    {
        if (!string.IsNullOrWhiteSpace(domain))
            return DomainName.Normalize(domain);

        if (kind == CacheInvalidationKind.Domain && !string.IsNullOrWhiteSpace(scopeLabel))
            return DomainName.Normalize(scopeLabel);

        if (kind is CacheInvalidationKind.Entity or CacheInvalidationKind.EntityKind)
        {
            int slash = scopeLabel.IndexOf('/');
            if (slash > 0)
                return DomainName.Normalize(scopeLabel[..slash]);
        }

        return null;
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
