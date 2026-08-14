using CacheOrchestrator.Cluster;
using CacheOrchestrator.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CacheOrchestrator.Bus;

/// <summary>
/// Maps cluster receive endpoints (independent of Local Admin <c>Enabled</c>).
/// </summary>
public static class ClusterLocalApi
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Maps <c>.../cluster/apply</c> and <c>.../cluster/info</c> when the HTTP bus is registered
    /// and <c>Cache:Cluster:Bus:Enabled</c> is true. Safe no-op otherwise.
    /// </summary>
    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        using IServiceScope scope = endpoints.ServiceProvider.CreateScope();
        CacheOrchestratorOptions opts = scope.ServiceProvider
            .GetRequiredService<IOptions<CacheOrchestratorOptions>>().Value;

        IClusterCommandBus bus = scope.ServiceProvider.GetRequiredService<IClusterCommandBus>();
        if (!opts.Cluster.Bus.Enabled || !bus.IsEnabled)
            return endpoints;

        string prefix = HttpClusterCommandBus.ResolveRoutePrefix(opts);

        if (string.IsNullOrEmpty(HttpClusterCommandBus.ResolveApiKey(opts)))
        {
            ILogger? logger = endpoints.ServiceProvider.GetService<ILoggerFactory>()
                ?.CreateLogger("CacheOrchestrator.Bus");
            logger?.LogWarning(
                "CacheOrchestrator cluster bus is enabled without ApiKey. " +
                "Receive endpoints at '{Prefix}/cluster' are open. " +
                "Set Cache:Cluster:Bus:ApiKey or Cache:Admin:ApiKey for non-local environments.",
                prefix);
        }

        RouteGroupBuilder group = endpoints
            .MapGroup(prefix)
            .AddEndpointFilter<ClusterEndpointAuth>()
            .WithTags("CacheOrchestrator Cluster")
            .WithMetadata(new OutputCacheAttribute { NoStore = true });

        group.MapPost("/cluster/apply", async (
            HttpRequest request,
            IClusterCommandHandler handler,
            IInstanceIdProvider instanceId,
            IOptionsMonitor<CacheOrchestratorOptions> options,
            CancellationToken cancellationToken) =>
        {
            ClusterCommand? command;
            try
            {
                command = await JsonSerializer
                    .DeserializeAsync<ClusterCommand>(request.Body, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { error = "Invalid cluster command JSON." });
            }

            if (command is null)
                return Results.BadRequest(new { error = "Request body is required." });

            if (string.IsNullOrWhiteSpace(command.OriginInstanceId)
                || string.IsNullOrWhiteSpace(command.Namespace))
            {
                return Results.BadRequest(new { error = "originInstanceId and namespace are required." });
            }

            string localNs = options.CurrentValue.Namespace ?? string.Empty;
            if (!string.Equals(command.Namespace, localNs, StringComparison.Ordinal))
            {
                return Results.Conflict(new
                {
                    error = "Namespace mismatch.",
                    commandNamespace = command.Namespace,
                    localNamespace = localNs
                });
            }

            if (string.Equals(command.OriginInstanceId, instanceId.InstanceId, StringComparison.Ordinal))
                return Results.Ok(new { applied = false, reason = "origin-is-self" });

            await handler.ApplyLocalAsync(command, cancellationToken).ConfigureAwait(false);
            return Results.Ok(new
            {
                applied = true,
                commandId = command.CommandId,
                commandType = command.GetType().Name
            });
        });

        // When Local Admin is enabled it already maps GET …/cluster/info (same prefix).
        // Skip a second registration to avoid ambiguous routes.
        if (!opts.Admin.Enabled)
        {
            group.MapGet("/cluster/info", async (
                IInstanceIdProvider instanceId,
                IClusterMembership membership,
                IClusterCommandBus busSvc,
                IOptionsMonitor<CacheOrchestratorOptions> options,
                CancellationToken cancellationToken) =>
            {
                IReadOnlyList<ClusterPeer> peers =
                    await membership.GetPeersAsync(cancellationToken).ConfigureAwait(false);

                return Results.Ok(new
                {
                    instanceId = instanceId.InstanceId,
                    @namespace = options.CurrentValue.Namespace,
                    busEnabled = busSvc.IsEnabled,
                    membership = membership.Kind,
                    peerCount = peers.Count,
                    peers = peers.Select(p => new { id = p.Id, url = p.BaseUrl.ToString() }).ToArray()
                });
            });
        }

        return endpoints;
    }
}
