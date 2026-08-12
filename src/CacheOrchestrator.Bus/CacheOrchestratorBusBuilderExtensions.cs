using CacheOrchestrator.Cluster;
using CacheOrchestrator.Configuration;
using CacheOrchestrator.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Bus;

/// <summary>
/// Registers the HTTP cluster command bus with CacheOrchestrator.
/// </summary>
public static class CacheOrchestratorBusBuilderExtensions
{
    /// <summary>
    /// Adds HTTP cluster command transport and membership (Static, ServiceDiscovery, or Null from configuration).
    /// Call inside the <c>AddCacheOrchestrator</c> builder callback so registration happens before
    /// core <c>TryAdd</c> Null defaults.
    /// </summary>
    /// <param name="builder">The CacheOrchestrator builder.</param>
    /// <param name="configSection">Configuration section (must match <c>AddCacheOrchestrator</c>). Default: <c>Cache</c>.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <remarks>
    /// <code>
    /// services.AddCacheOrchestrator(configuration, o =&gt; o.AddHttpClusterBus());
    /// app.MapCacheOrchestratorHttpBus();
    /// </code>
    /// When <c>Cache:Cluster:Bus:Enabled</c> is false, the registered bus reports
    /// <see cref="IClusterCommandBus.IsEnabled"/> = false and does not call peers.
    /// For <c>Membership=ServiceDiscovery</c>, this also calls <c>AddServiceDiscovery()</c>.
    /// </remarks>
    public static ICacheOrchestratorBuilder AddHttpClusterBus(
        this ICacheOrchestratorBuilder builder,
        string configSection = "Cache")
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(configSection);

        builder.Services.AddHttpClient(HttpClusterCommandBus.HttpClientName);

        // ServiceDiscovery needs IConfiguration (config-based endpoint provider).
        builder.Services.TryAddSingleton<IConfiguration>(builder.Configuration);

        // ServiceDiscovery resolver is cheap to register; only used when Membership=ServiceDiscovery.
        builder.Services.AddServiceDiscovery();

        // Replace Null defaults registered later via TryAdd (we register first in builder callback).
        builder.Services.AddSingleton<IClusterMembership>(sp =>
        {
            CacheOrchestratorOptions opts = sp.GetRequiredService<IOptions<CacheOrchestratorOptions>>().Value;
            string membership = opts.Cluster.Bus.Membership ?? "Null";
            if (string.Equals(membership, "Static", StringComparison.OrdinalIgnoreCase))
                return ActivatorUtilities.CreateInstance<StaticClusterMembership>(sp);

            if (string.Equals(membership, "ServiceDiscovery", StringComparison.OrdinalIgnoreCase))
                return ActivatorUtilities.CreateInstance<ServiceDiscoveryClusterMembership>(sp);

            return NullClusterMembership.Instance;
        });

        builder.Services.AddSingleton<IClusterCommandBus, HttpClusterCommandBus>();
        builder.Services.TryAddSingleton<ClusterEndpointAuth>();

        return builder;
    }
}
