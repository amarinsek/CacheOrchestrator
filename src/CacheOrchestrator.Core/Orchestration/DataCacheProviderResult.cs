namespace CacheOrchestrator.Orchestration;

/// <summary>Describes how a provider satisfied a Data Cache request.</summary>
public enum DataCacheProviderOutcome
{
    /// <summary>
    /// The provider did not report an outcome. Providers must never return this value.
    /// </summary>
    Unknown = 0,

    /// <summary>The returned value was a fresh cache hit.</summary>
    Cached = 1,

    /// <summary>The returned value was produced by this call's completed factory invocation.</summary>
    Materialized = 2,

    /// <summary>The returned value was stale because a refresh could not complete synchronously.</summary>
    Stale = 3,
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

    /// <summary>How the provider satisfied the request.</summary>
    public DataCacheProviderOutcome Outcome { get; }
}
