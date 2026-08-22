namespace CacheOrchestrator.FusionCache;

/// <summary>
/// A single entity reference used in an <see cref="EntityFootprint"/> (kind + resource id).
/// </summary>
/// <param name="EntityKind">Resource type within the domain (e.g. <c>products</c>).</param>
/// <param name="ResourceId">Stable business id for that kind.</param>
public readonly record struct EntityRef(string EntityKind, string ResourceId);