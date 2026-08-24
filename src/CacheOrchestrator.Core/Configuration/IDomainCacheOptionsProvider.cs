namespace CacheOrchestrator.Configuration;

/// <summary>
/// Resolves effective per-domain cache options (process-wide, no HTTP request required).
/// </summary>
public interface IDomainCacheOptionsProvider
{
    /// <summary>
    /// Returns domain options from the process-wide cache (or creates them).
    /// Useful in contexts where no HTTP request is active, such as invalidation.
    /// </summary>
    /// <param name="domain">Raw domain name (normalized internally).</param>
    /// <returns>Effective domain options (never null).</returns>
    DomainCacheOptions GetOrCreateDomainOptions(string domain);
}
