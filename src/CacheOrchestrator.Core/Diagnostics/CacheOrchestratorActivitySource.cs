using System.Diagnostics;

namespace CacheOrchestrator.Diagnostics;

/// <summary>
/// Shared <see cref="ActivitySource"/> for CacheOrchestrator OpenTelemetry activities.
/// </summary>
public static class CacheOrchestratorActivitySource
{
    /// <summary>Activity source name (subscribe with this value).</summary>
    public const string Name = "CacheOrchestrator";

    /// <summary>Shared activity source instance.</summary>
    public static readonly ActivitySource Source = new(Name, "1.0.0");
}
