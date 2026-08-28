namespace CacheOrchestrator.Orchestration;

/// <summary>Describes whether a provider returned cached data or the value from this call's factory.</summary>
public enum DataCacheProviderOutcome
{
    /// <summary>The returned value was already cached, including a stale value returned during refresh.</summary>
    Cached = 0,

    /// <summary>The returned value was produced by this call's completed factory invocation.</summary>
    Materialized = 1,
}

/// <summary>A Data Cache provider value together with its materialization outcome.</summary>
/// <typeparam name="T">Stored value type.</typeparam>
public readonly struct DataCacheProviderResult<T>
{
    /// <summary>Creates a provider result.</summary>
    public DataCacheProviderResult(T value, DataCacheProviderOutcome outcome)
    {
        Value = value;
        Outcome = outcome;
    }

    /// <summary>The returned cache value.</summary>
    public T Value { get; }

    /// <summary>Whether the value was cached or materialized by this call.</summary>
    public DataCacheProviderOutcome Outcome { get; }
}
