using CacheOrchestrator.Edge.Configuration;
using CacheOrchestrator.Edge.Invalidation;
using CacheOrchestrator.Edge.Providers;
using CacheOrchestrator.Edge.Responses;
using CacheOrchestrator.Edge.Tags;
using CacheOrchestrator.Invalidation;
using CacheOrchestrator.OutputCache;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Edge.DependencyInjection;

/// <summary>Registers provider-neutral edge orchestration services.</summary>
public static class EdgeServiceCollectionExtensions
{
    /// <summary>Registers tag-native edge orchestration and configured providers.</summary>
    public static IServiceCollection AddCacheOrchestratorEdge(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<ICacheOrchestratorEdgeBuilder>? configure = null,
        string configSection = "Cache")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(configSection);

        var builder = new DefaultCacheOrchestratorEdgeBuilder(services, configuration, configSection);
        configure?.Invoke(builder);

        if (services.Any(static descriptor => descriptor.ServiceType == typeof(EdgeRegistrationMarker)))
            return services;

        services.AddSingleton<EdgeRegistrationMarker>();
        services.AddOptions<CacheOrchestratorEdgeOptions>()
            .Bind(configuration.GetSection(configSection))
            .ValidateOnStart();

        CacheOrchestratorEdgeOptions bound = new();
        configuration.GetSection(configSection).Bind(bound);
        services.AddSingleton(new EdgeInvalidationChannel(Math.Max(1, bound.EdgeQueue.Capacity)));

        services.TryAddSingleton<EdgeProviderCatalog>();
        services.TryAddSingleton<EdgeInstanceResolver>();
        services.TryAddSingleton<IDomainEdgeOptionsProvider, DomainEdgeOptionsProvider>();
        services.TryAddSingleton<EdgeTagProjector>();
        services.TryAddSingleton<IEdgeInvalidationQueue, ChannelEdgeInvalidationQueue>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<CacheOrchestratorEdgeOptions>, CacheOrchestratorEdgeOptionsValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICacheResponseContributor, EdgeResponseContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICacheInvalidationObserver, EdgeInvalidationObserver>());
        services.AddHostedService<EdgeInvalidationWorker>();
        return services;
    }
}

internal sealed class EdgeRegistrationMarker;
