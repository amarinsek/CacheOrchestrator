using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CacheOrchestrator.DependencyInjection;

/// <summary>
/// Minimal host builder surface for satellite packages that only need Services + Configuration
/// (no ASP.NET Output Cache / backend registration).
/// </summary>
public interface ICacheOrchestratorServiceBuilder
{
    /// <summary>The application service collection.</summary>
    IServiceCollection Services { get; }

    /// <summary>The application configuration.</summary>
    IConfiguration Configuration { get; }
}
