using CacheOrchestrator.Cluster;
using CacheOrchestrator.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.HttpBus;

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
    /// <c>Microsoft.Extensions.ServiceDiscovery</c> is registered only when
    /// <c>Membership=ServiceDiscovery</c> (so hosts using Null/Static do not load that assembly at startup).
    /// </remarks>
    public static ICacheOrchestratorBuilder AddHttpClusterBus(
        this ICacheOrchestratorBuilder builder,
        string configSection = "Cache")
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(configSection);

        builder.Services.AddHttpClient(HttpClusterCommandBus.HttpClientName);
        builder.Services.AddOptions<HttpBusOptions>()
            .Bind(builder.Configuration.GetSection(configSection))
            .ValidateOnStart();
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<HttpBusOptions>, HttpBusOptionsValidator>());

        // Only wire ServiceDiscovery when configured — avoids requiring that assembly for Null/Static hosts.
        string membership = builder.Configuration[$"{configSection}:Cluster:Bus:Membership"] ?? "Null";
        if (string.Equals(membership, "ServiceDiscovery", StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.TryAddSingleton<IConfiguration>(builder.Configuration);
            builder.Services.AddServiceDiscovery();
        }

        // Replace Null defaults registered later via TryAdd (we register first in builder callback).
        builder.Services.AddSingleton<IClusterMembership>(CreateMembership);

        builder.Services.AddSingleton<IClusterCommandBus, HttpClusterCommandBus>();
        builder.Services.TryAddSingleton<ClusterEndpointAuth>();

        return builder;
    }

    /// <summary>
    /// Resolves membership. ServiceDiscovery path is a separate method so Null/Static hosts never
    /// JIT-load <see cref="ServiceDiscoveryClusterMembership"/> (and thus its assembly).
    /// </summary>
    private static IClusterMembership CreateMembership(IServiceProvider sp)
    {
        HttpBusOptions opts = sp.GetRequiredService<IOptions<HttpBusOptions>>().Value;
        string membership = opts.Cluster.Bus.Membership ?? "Null";

        if (string.Equals(membership, "Static", StringComparison.OrdinalIgnoreCase))
            return ActivatorUtilities.CreateInstance<StaticClusterMembership>(sp);

        if (string.Equals(membership, "ServiceDiscovery", StringComparison.OrdinalIgnoreCase))
            return CreateServiceDiscoveryMembership(sp);

        return NullClusterMembership.Instance;
    }

    // Keep SD types out of CreateMembership's IL so Null/Static does not load that package.
    private static IClusterMembership CreateServiceDiscoveryMembership(IServiceProvider sp)
        => ActivatorUtilities.CreateInstance<ServiceDiscoveryClusterMembership>(sp);
}
