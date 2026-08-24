namespace CacheOrchestrator.Configuration;

/// <summary>
/// Soft/hard timeouts and circuit breaker for <strong>distributed</strong> FusionCache L2 operations
/// (any non-InMemory provider: Redis package, SQL, custom).
/// </summary>
/// <remarks>
/// Bound from <c>Cache:Distributed</c>. Not applied when the FusionCache instance provider is <c>InMemory</c>.
/// </remarks>
public sealed class DistributedResilienceOptions
{
    /// <summary>Distributed cache soft timeout in seconds. Default: 1.</summary>
    public int SoftTimeoutSeconds { get; set; } = 1;

    /// <summary>Distributed cache hard timeout in seconds. Default: 2.</summary>
    public int HardTimeoutSeconds { get; set; } = 2;

    /// <summary>Circuit breaker duration in seconds after distributed failures. Default: 5.</summary>
    public int CircuitBreakerSeconds { get; set; } = 5;

    /// <summary>Returns <see langword="true"/> when all values match factory defaults.</summary>
    public bool IsFactoryDefault =>
        SoftTimeoutSeconds == 1 && HardTimeoutSeconds == 2 && CircuitBreakerSeconds == 5;
}
