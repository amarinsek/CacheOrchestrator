using CacheOrchestrator.Cluster;
using CacheOrchestrator.Configuration;
using CacheOrchestrator.Diagnostics;
using CacheOrchestrator.Invalidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Admin;

/// <summary>
/// Maps Local Admin API endpoints (per instance). No-op when Admin is disabled.
/// Prefer <see cref="DependencyInjection.ApplicationBuilderExtensions.MapCacheOrchestratorAdmin"/>.
/// </summary>
public static class AdminLocalApi
{
    /// <summary>
    /// Maps Local Admin routes under <c>Cache:Admin:RoutePrefix</c> when Admin is enabled.
    /// Safe to call when Admin is disabled (maps nothing).
    /// </summary>
    /// <param name="endpoints">Endpoint route builder.</param>
    /// <returns>The same <paramref name="endpoints"/> for chaining.</returns>
    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        using IServiceScope scope = endpoints.ServiceProvider.CreateScope();
        CacheOrchestratorOptions opts = scope.ServiceProvider
            .GetRequiredService<IOptions<CacheOrchestratorOptions>>().Value;

        if (!opts.Admin.Enabled)
            return endpoints;

        string prefix = string.IsNullOrWhiteSpace(opts.Admin.RoutePrefix)
            ? "/cache-admin/local"
            : opts.Admin.RoutePrefix.TrimEnd('/');

        if (string.IsNullOrEmpty(opts.Admin.ApiKey))
        {
            ILogger? logger = endpoints.ServiceProvider.GetService<ILoggerFactory>()
                ?.CreateLogger("CacheOrchestrator.Admin");
            logger?.LogWarning(
                "CacheOrchestrator Admin is enabled without ApiKey. Local Admin API at '{Prefix}' is open. " +
                "Set Cache:Admin:ApiKey for non-local environments.",
                prefix);
        }

        // Admin is operational traffic — never store responses in Output Cache.
        RouteGroupBuilder group = endpoints
            .MapGroup(prefix)
            .AddEndpointFilter<AdminApiKeyEndpointFilter>()
            .WithTags("CacheOrchestrator Admin")
            .WithMetadata(new OutputCacheAttribute { NoStore = true });

        group.MapGet("/health", async (AdminQueryService query, CancellationToken cancellationToken) =>
            Results.Ok(await query.GetHealthAsync(cancellationToken)));

        // Always available when Local Admin is on (even without CacheOrchestrator.HttpBus).
        // Prevents SPA MapFallbackToFile HTML from being mistaken for JSON on probe misses.
        group.MapGet("/cluster/info", async (
            IInstanceIdProvider instanceId,
            IClusterMembership membership,
            IClusterCommandBus bus,
            IOptionsMonitor<CacheOrchestratorOptions> options,
            CancellationToken cancellationToken) =>
        {
            IReadOnlyList<ClusterPeer> peers =
                await membership.GetPeersAsync(cancellationToken).ConfigureAwait(false);

            return Results.Ok(new
            {
                instanceId = instanceId.InstanceId,
                @namespace = options.CurrentValue.Namespace,
                busEnabled = bus.IsEnabled,
                membership = membership.Kind,
                peerCount = peers.Count,
                peers = peers.Select(p => new { id = p.Id, url = p.BaseUrl.ToString() }).ToArray()
            });
        });

        // Obsolete: process-lifetime counters. Prefer OTEL/Prometheus for analytics.
        // Kept for external tools; Admin Console stats UI uses Prometheus only.
#pragma warning disable CS0618
        group.MapGet("/stats", (AdminQueryService query) => Results.Ok(query.GetStats()));
#pragma warning restore CS0618

        group.MapGet("/endpoints", (AdminQueryService query) => Results.Ok(query.GetEndpoints()));

        group.MapGet("/domains", (AdminQueryService query) => Results.Ok(query.GetDomains()));

        group.MapGet("/domains/{domain}", (string domain, AdminQueryService query) =>
        {
            AdminDomainConfigDto? dto = query.GetDomain(domain);
            return dto is null ? Results.NotFound() : Results.Ok(dto);
        });

        group.MapPost("/invalidate", async (
            AdminInvalidateRequest body,
            ICacheOrchestratorInvalidator invalidator,
            CancellationToken cancellationToken) =>
        {
            if (body is null)
                return Results.BadRequest(new { error = "Request body is required." });

            string scope = (body.Scope ?? "domain").Trim().ToLowerInvariant();

            // distribute=false (default): local only. distribute=true: invalidator may publish when bus enabled.
            using IDisposable? localOnly = body.Distribute ? null : ClusterCommandScope.EnterLocalOnly();

            CacheInvalidationResult result;
            switch (scope)
            {
                case "domain":
                    if (string.IsNullOrWhiteSpace(body.Domain))
                        return Results.BadRequest(new { error = "domain is required for scope=domain." });
                    result = await invalidator.InvalidateDomainAsync(body.Domain, cancellationToken)
                        .ConfigureAwait(false);
                    break;

                case "entity":
                    if (string.IsNullOrWhiteSpace(body.Domain)
                        || string.IsNullOrWhiteSpace(body.EntityKind)
                        || string.IsNullOrWhiteSpace(body.EntityId))
                    {
                        return Results.BadRequest(new
                        {
                            error = "domain, entityKind, and entityId are required for scope=entity."
                        });
                    }

                    result = await invalidator.InvalidateEntityAsync(
                            body.Domain,
                            body.EntityKind,
                            body.EntityId,
                            cancellationToken)
                        .ConfigureAwait(false);
                    break;

                case "entitykind":
                    if (string.IsNullOrWhiteSpace(body.Domain) || string.IsNullOrWhiteSpace(body.EntityKind))
                    {
                        return Results.BadRequest(new
                        {
                            error = "domain and entityKind are required for scope=entityKind."
                        });
                    }

                    result = await invalidator.InvalidateEntityKindAsync(
                            body.Domain,
                            body.EntityKind,
                            cancellationToken)
                        .ConfigureAwait(false);
                    break;

                case "tags":
                    if (body.Tags is null || body.Tags.Length == 0)
                        return Results.BadRequest(new { error = "tags are required for scope=tags." });
                    result = await invalidator.InvalidateTagsAsync(body.Tags, cancellationToken)
                        .ConfigureAwait(false);
                    break;

                default:
                    return Results.BadRequest(new { error = "scope must be domain, entity, entityKind, or tags." });
            }

            if (body.Distribute
                && result.ClusterPublish is { AllSucceeded: false } publish)
            {
                return ClusterPublishIncomplete(
                    domain: body.Domain,
                    publish,
                    payload: result);
            }

            return Results.Ok(result);
        });

        group.MapPost("/domains/{domain}/version", async (
            string domain,
            AdminVersionRequest? body,
            IDomainRuntimeOverrideStore overrides,
            AdminQueryService query,
            IClusterCommandBus bus,
            ClusterCommandFactory commands,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(domain))
                return Results.BadRequest(new { error = "domain is required." });

            string? requested = body?.Version;
            string version = string.IsNullOrWhiteSpace(requested)
                ? "rt-" + DateTimeOffset.UtcNow.UtcTicks.ToString("x")
                : requested.Trim();

            overrides.SetVersion(domain, version);

            IResult? publishConflict = null;
            if (body?.Distribute == true && bus.IsEnabled)
            {
                VersionBumpCommand cmd = commands.CreateVersionBump(domain, version);
                publishConflict = await PublishMutationOrConflictAsync(
                        bus,
                        cmd,
                        nameof(VersionBumpCommand),
                        domain,
                        loggerFactory,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            AdminDomainConfigDto effective = query.GetDomainConfig(DomainName.Normalize(domain));
            AdminDomainMutationResultDto ok = new()
            {
                Domain = effective.Name,
                Effective = effective
            };
            return publishConflict ?? Results.Ok(ok);
        });

#pragma warning disable CS0618 // AdminTtlPatchRequest / DomainTtlPatch kept for compatibility
        group.MapMethods("/domains/{domain}/ttl", ["PATCH"], async (
            string domain,
            AdminTtlPatchRequest body,
            IDomainRuntimeOverrideStore overrides,
            AdminQueryService query,
            IClusterCommandBus bus,
            ClusterCommandFactory commands,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(domain))
                return Results.BadRequest(new { error = "domain is required." });
            if (body is null)
                return Results.BadRequest(new { error = "Request body is required." });

            DomainSettingsPatch patch = DomainSettingsPatchMapper.FromTtlRequest(body);
            if (!patch.HasAny)
                return Results.BadRequest(new { error = "At least one TTL field is required." });

            string? validationError = ValidateSettingsPatch(patch);
            if (validationError is not null)
                return Results.BadRequest(new { error = validationError });

            overrides.PatchSettings(domain, patch);

            IResult? publishConflict = null;
            if (body.Distribute && bus.IsEnabled)
            {
                // Prefer legacy ttlPatch when TTL-only so older peers still apply.
                DomainTtlPatch ttl = ToTtlPatch(patch);
                TtlPatchCommand cmd = commands.CreateTtlPatch(domain, ttl);
                publishConflict = await PublishMutationOrConflictAsync(
                        bus,
                        cmd,
                        nameof(TtlPatchCommand),
                        domain,
                        loggerFactory,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            AdminDomainConfigDto effective = query.GetDomainConfig(DomainName.Normalize(domain));
            AdminDomainMutationResultDto ok = new()
            {
                Domain = effective.Name,
                Effective = effective
            };
            return publishConflict ?? Results.Ok(ok);
        });
#pragma warning restore CS0618

        group.MapGet("/domain-settings/catalog", () =>
            Results.Ok(new AdminDomainSettingsCatalogDto
            {
                Settings = DomainSettingCatalog.GetEntries(),
            }));

        group.MapMethods("/domains/{domain}/settings", ["PATCH"], async (
            string domain,
            AdminSettingsPatchRequest body,
            IDomainRuntimeOverrideStore overrides,
            AdminQueryService query,
            IClusterCommandBus bus,
            ClusterCommandFactory commands,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(domain))
                return Results.BadRequest(new { error = "domain is required." });
            if (body?.Settings is null || body.Settings.Count == 0)
                return Results.BadRequest(new { error = "settings must contain at least one entry." });

            DomainSettingsPatch patch;
            try
            {
                patch = DomainSettingsPatchMapper.FromDictionary(body.Settings);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            string? validationError = ValidateSettingsPatch(patch);
            if (validationError is not null)
                return Results.BadRequest(new { error = validationError });

            overrides.PatchSettings(domain, patch);

            IResult? publishConflict = null;
            if (body.Distribute && bus.IsEnabled)
            {
                ClusterCommand cmd;
                string metricName;
                if (patch.IsTtlOnly)
                {
#pragma warning disable CS0618
                    DomainTtlPatch ttl = ToTtlPatch(patch);
                    cmd = commands.CreateTtlPatch(domain, ttl);
#pragma warning restore CS0618
                    metricName = nameof(TtlPatchCommand);
                }
                else
                {
                    cmd = commands.CreateSettingsPatch(domain, body.Settings);
                    metricName = nameof(SettingsPatchCommand);
                }

                publishConflict = await PublishMutationOrConflictAsync(
                        bus,
                        cmd,
                        metricName,
                        domain,
                        loggerFactory,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            AdminDomainConfigDto effective = query.GetDomainConfig(DomainName.Normalize(domain));
            AdminDomainMutationResultDto ok = new()
            {
                Domain = effective.Name,
                Effective = effective
            };
            return publishConflict ?? Results.Ok(ok);
        });

        return endpoints;
    }

    /// <summary>
    /// Publishes a mutation command. Returns HTTP 409 when any peer failed (local already applied);
    /// otherwise <see langword="null"/> so the caller can return 200 with the success payload.
    /// </summary>
    private static async Task<IResult?> PublishMutationOrConflictAsync(
        IClusterCommandBus bus,
        ClusterCommand command,
        string metricName,
        string domain,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            ClusterPublishResult published = await bus.PublishAsync(command, cancellationToken)
                .ConfigureAwait(false);
            CacheOrchestratorMetrics.RecordClusterPublished(metricName);
            if (published.AllSucceeded)
                return null;

            return ClusterPublishIncomplete(domain, published, payload: null);
        }
        catch (Exception ex)
        {
            CacheOrchestratorMetrics.RecordClusterPublishFailure("exception");
            loggerFactory.CreateLogger("CacheOrchestrator.Admin")
                .LogWarning(ex, "Cluster publish failed for {Command} on domain {Domain}", metricName, domain);
            return ClusterPublishIncomplete(
                domain,
                new ClusterPublishResult(
                [
                    new ClusterPeerPublishOutcome
                    {
                        PeerId = "(bus)",
                        Succeeded = false,
                        Error = ex.Message,
                    },
                ]),
                payload: null);
        }
    }

    private static IResult ClusterPublishIncomplete(
        string? domain,
        ClusterPublishResult publish,
        object? payload)
    {
        var peerFailures = publish.Failures
            .Select(f => new { peerId = f.PeerId, error = f.Error })
            .ToArray();

        return Results.Json(
            new
            {
                error = "Cluster publish incomplete.",
                domain,
                localApplied = true,
                peerFailures,
                result = payload,
            },
            statusCode: StatusCodes.Status409Conflict);
    }

    private static string? ValidateSettingsPatch(DomainSettingsPatch patch)
    {
        static bool NegativeTs(TimeSpan? v) => v is { } t && t < TimeSpan.Zero;
        static bool Negative(int? v) => v is < 0;

        if (NegativeTs(patch.OutputCacheTtl)
            || NegativeTs(patch.DataCacheTtl)
            || NegativeTs(patch.HardTtl)
            || NegativeTs(patch.FailSafe)
            || NegativeTs(patch.ClientTtl)
            || NegativeTs(patch.ClientTtlMin)
            || NegativeTs(patch.Jitter)
            || NegativeTs(patch.FactorySoftTimeout)
            || NegativeTs(patch.FactoryHardTimeout)
            || Negative(patch.MaxItemBytes))
        {
            return "Numeric settings must be non-negative.";
        }

        if (patch.ClientTtl is TimeSpan max
            && patch.ClientTtlMin is TimeSpan min
            && min > max)
        {
            return "clientCache.ttlMin must be <= clientCache.ttl when both are set.";
        }

        if (patch.EagerRefreshRatio is double r && (r < 0 || r >= 1))
            return "dataCache.eagerRefreshRatio must be in [0, 1).";

        return null;
    }

#pragma warning disable CS0618
    private static DomainTtlPatch ToTtlPatch(DomainSettingsPatch patch) =>
        new()
        {
            OutputCacheTtlSeconds = ToSeconds(patch.OutputCacheTtl),
            DataCacheTtlSeconds = ToSeconds(patch.DataCacheTtl),
            HardTtlSeconds = ToSeconds(patch.HardTtl),
            FailSafeSeconds = ToSeconds(patch.FailSafe),
            ClientTtlSeconds = ToSeconds(patch.ClientTtl),
            ClientTtlMinSeconds = ToSeconds(patch.ClientTtlMin),
        };
#pragma warning restore CS0618

    private static int? ToSeconds(TimeSpan? value) =>
        value is TimeSpan t ? (int)Math.Round(t.TotalSeconds) : null;
}
