namespace CacheOrchestrator.Diagnostics;

/// <summary>
/// Backend-specific readiness probe used by CacheOrchestrator health checks.
/// </summary>
public interface ICacheOrchestratorHealthProbe
{
    /// <summary>
    /// Stable probe name (e.g. <c>inmemory</c>, <c>redis</c>).
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Throws if the dependency is not usable; otherwise completes successfully.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ProbeAsync(CancellationToken cancellationToken = default);
}
