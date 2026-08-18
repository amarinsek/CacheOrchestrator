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

        group.MapGet("/health", (AdminQueryService query) => Results.Ok(query.GetHealth()));

        // Always available when Local Admin is on (even without CacheOrchestrator.Bus).
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

            if (body?.Distribute == true && bus.IsEnabled)
            {
                VersionBumpCommand cmd = commands.CreateVersionBump(domain, version);
                try
                {
                    await bus.PublishAsync(cmd, cancellationToken).ConfigureAwait(false);
                    CacheOrchestratorMetrics.RecordClusterPublished(nameof(VersionBumpCommand));
                }
                catch (Exception ex)
                {
                    CacheOrchestratorMetrics.RecordClusterPublishFailure("exception");
                    loggerFactory.CreateLogger("CacheOrchestrator.Admin")
                        .LogWarning(ex, "Cluster publish failed for VersionBump on domain {Domain}", domain);
                }
            }

            AdminDomainConfigDto effective = query.GetDomainConfig(DomainName.Normalize(domain));
            return Results.Ok(new AdminDomainMutationResultDto
            {
                Domain = effective.Name,
                Effective = effective
            });
        });

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

            DomainTtlPatch patch = new()
            {
                OutputCacheTtlSeconds = body.OutputCacheTtlSeconds,
                FusionCacheSoftTtlSeconds = body.FusionCacheSoftTtlSeconds,
                FusionCacheHardTtlSeconds = body.FusionCacheHardTtlSeconds,
                FusionCacheFailSafeSeconds = body.FusionCacheFailSafeSeconds,
                ClientTtlSeconds = body.ClientTtlSeconds,
                ClientTtlMinSeconds = body.ClientTtlMinSeconds
            };

            if (!patch.HasAny)
                return Results.BadRequest(new { error = "At least one TTL field is required." });

            string? validationError = ValidateTtlPatch(patch);
            if (validationError is not null)
                return Results.BadRequest(new { error = validationError });

            overrides.PatchTtl(domain, patch);

            if (body.Distribute && bus.IsEnabled)
            {
                TtlPatchCommand cmd = commands.CreateTtlPatch(domain, patch);
                try
                {
                    await bus.PublishAsync(cmd, cancellationToken).ConfigureAwait(false);
                    CacheOrchestratorMetrics.RecordClusterPublished(nameof(TtlPatchCommand));
                }
                catch (Exception ex)
                {
                    CacheOrchestratorMetrics.RecordClusterPublishFailure("exception");
                    loggerFactory.CreateLogger("CacheOrchestrator.Admin")
                        .LogWarning(ex, "Cluster publish failed for TtlPatch on domain {Domain}", domain);
                }
            }

            AdminDomainConfigDto effective = query.GetDomainConfig(DomainName.Normalize(domain));
            return Results.Ok(new AdminDomainMutationResultDto
            {
                Domain = effective.Name,
                Effective = effective
            });
        });

        return endpoints;
    }

    private static string? ValidateTtlPatch(DomainTtlPatch patch)
    {
        static bool Negative(int? v) => v is < 0;

        if (Negative(patch.OutputCacheTtlSeconds)
            || Negative(patch.FusionCacheSoftTtlSeconds)
            || Negative(patch.FusionCacheHardTtlSeconds)
            || Negative(patch.FusionCacheFailSafeSeconds)
            || Negative(patch.ClientTtlSeconds)
            || Negative(patch.ClientTtlMinSeconds))
        {
            return "TTL values must be non-negative.";
        }

        if (patch.ClientTtlSeconds is int max
            && patch.ClientTtlMinSeconds is int min
            && min > max)
        {
            return "clientTtlMinSeconds must be <= clientTtlSeconds when both are set.";
        }

        return null;
    }
}
