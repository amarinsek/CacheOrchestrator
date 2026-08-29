namespace CacheOrchestrator.Admin;

/// <summary>Immutable inputs for recording application factory work.</summary>
public sealed class AdminFactoryRecord
{
    /// <summary>Endpoint identity, or null when unavailable.</summary>
    public string? EndpointKey { get; init; }

    /// <summary>Normalized domain, or null when unresolved.</summary>
    public string? Domain { get; init; }

    /// <summary>Whether the factory failed.</summary>
    public bool Failed { get; init; }

    /// <summary>Optional duration in <see cref="System.Diagnostics.Stopwatch"/> ticks.</summary>
    public long? ElapsedTicks { get; init; }

    /// <summary>Optional estimated result size.</summary>
    public long? ResultSizeBytes { get; init; }
}
