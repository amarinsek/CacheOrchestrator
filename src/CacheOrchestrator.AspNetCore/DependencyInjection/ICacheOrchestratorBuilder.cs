using CacheOrchestrator.Backends;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CacheOrchestrator.DependencyInjection;

/// <summary>
/// A builder for configuring CacheOrchestrator and registering custom backend providers.
/// </summary>
public interface ICacheOrchestratorBuilder : ICacheOrchestratorServiceBuilder
{

    /// <summary>
    /// Registers a custom backend provider registrar.
    /// </summary>
    /// <param name="registrar">The backend registrar implementation.</param>
    /// <returns>The builder instance.</returns>
    ICacheOrchestratorBuilder AddBackend(ICacheBackendRegistrar registrar);

    /// <summary>
    /// Adds a callback that configures the shared ASP.NET Core <see cref="OutputCacheOptions"/>
    /// (after built-in base policy and backend defaults such as InMemory size limits).
    /// </summary>
    /// <param name="configure">Configuration callback.</param>
    /// <returns>The builder instance.</returns>
    ICacheOrchestratorBuilder ConfigureOutputCache(Action<OutputCacheOptions> configure);
}
