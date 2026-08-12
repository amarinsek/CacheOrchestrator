using CacheOrchestrator.Cluster;
using CacheOrchestrator.Configuration;

namespace CacheOrchestrator.Admin;

/// <summary>Resolves the process instance identifier (<c>Cache:InstanceId</c>).</summary>
internal static class AdminInstanceId
{
    /// <summary>Resolves from root cache options (not Admin subsection).</summary>
    public static string Resolve(CacheOrchestratorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return DefaultInstanceIdProvider.Resolve(options.InstanceId);
    }
}
