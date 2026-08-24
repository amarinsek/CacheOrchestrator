using CacheOrchestrator.Configuration;
using Microsoft.Extensions.Options;

namespace CacheOrchestrator.Cluster;

/// <summary>
/// Resolves <c>Cache:InstanceId</c> once at startup (stable for the process lifetime).
/// </summary>
internal sealed class DefaultInstanceIdProvider : IInstanceIdProvider
{
    public DefaultInstanceIdProvider(IOptions<CacheOrchestratorOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        InstanceId = Resolve(options.Value.InstanceId);
    }

    /// <inheritdoc />
    public string InstanceId { get; }

    /// <summary>Resolves a configured id or machine name (shared with tests / Admin).</summary>
    internal static string Resolve(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.Trim();

        try
        {
            return Environment.MachineName;
        }
        catch
        {
            return "unknown";
        }
    }
}
