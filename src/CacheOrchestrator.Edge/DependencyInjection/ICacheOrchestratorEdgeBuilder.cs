using CacheOrchestrator.DependencyInjection;

namespace CacheOrchestrator.Edge.DependencyInjection;

/// <summary>Builder surface used by tag-native edge provider packages.</summary>
public interface ICacheOrchestratorEdgeBuilder : ICacheOrchestratorServiceBuilder
{
    /// <summary>Root configuration section containing edge settings.</summary>
    string ConfigSection { get; }
}

internal sealed class DefaultCacheOrchestratorEdgeBuilder : ICacheOrchestratorEdgeBuilder
{
    public DefaultCacheOrchestratorEdgeBuilder(
        Microsoft.Extensions.DependencyInjection.IServiceCollection services,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        string configSection)
    {
        Services = services;
        Configuration = configuration;
        ConfigSection = configSection;
    }

    public Microsoft.Extensions.DependencyInjection.IServiceCollection Services { get; }
    public Microsoft.Extensions.Configuration.IConfiguration Configuration { get; }
    public string ConfigSection { get; }
}
