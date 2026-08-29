using CacheOrchestrator.Entity;

namespace CacheOrchestrator.Orchestration;

/// <summary>
/// Provider-agnostic data-cache request: domain policy, logical key, and invalidation footprint.
/// </summary>
/// <remarks>
/// The orchestrator prefixes the physical key with domain + Version hex and always attaches
/// <c>domain:{name}</c> tags (plus footprint tags). Callers supply stable <see cref="Key"/> material
/// only — not HTTP route/vary hashing (that stays in the ASP.NET projection).
/// </remarks>
public sealed class CacheEntryRequest
{
    /// <summary>Cache domain name (normalized by the orchestrator).</summary>
    public required string Domain { get; init; }

    /// <summary>
    /// Caller-supplied key material (already stable for the entry).
    /// Must not be null or whitespace.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>Allows trusted adapters to pass a fully constructed provider key.</summary>
    internal bool KeyIsPhysical { get; init; }

    /// <summary>Receives the provider's result for trusted adapters that expose cache disposition.</summary>
    internal Action<DataCacheProviderOutcome>? OutcomeObserver { get; init; }

    /// <summary>
    /// Invalidation footprint. When null, only the domain tag is applied.
    /// </summary>
    public EntityFootprint? Footprint { get; init; }

    /// <summary>
    /// Extra tags beyond <see cref="Footprint"/> (advanced). Null or empty is ignored.
    /// </summary>
    public IReadOnlyList<string>? AdditionalTags { get; init; }
}
