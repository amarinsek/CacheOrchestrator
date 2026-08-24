using CacheOrchestrator.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.DependencyInjection;

/// <summary>
/// Ensures <see cref="CacheOrchestratorOptions"/> is bound from configuration at most once.
/// A second <c>Bind</c> appends to list properties (e.g. <c>Cluster:Bus:Static:Instances</c>).
/// </summary>
internal static class CacheOrchestratorOptionsBinding
{
    /// <summary>
    /// Registers configuration binding for <see cref="CacheOrchestratorOptions"/> if not already registered.
    /// </summary>
    /// <returns>
    /// The options builder (existing or newly created). Callers may chain
    /// <c>ValidateOnStart</c> / validators; those remain idempotent via DI.
    /// </returns>
    public static OptionsBuilder<CacheOrchestratorOptions> EnsureBound(
        IServiceCollection services,
        IConfiguration configuration,
        string configSection)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(configSection);

        OptionsBuilder<CacheOrchestratorOptions> builder = services.AddOptions<CacheOrchestratorOptions>();

        for (int i = 0; i < services.Count; i++)
        {
            if (services[i].ServiceType == typeof(CacheOrchestratorOptionsBindingMarker))
                return builder;
        }

        services.AddSingleton<CacheOrchestratorOptionsBindingMarker>();
        builder.Bind(configuration.GetSection(configSection));
        return builder;
    }
}

/// <summary>DI marker for single configuration bind of <see cref="CacheOrchestratorOptions"/>.</summary>
internal sealed class CacheOrchestratorOptionsBindingMarker;
