namespace CacheOrchestrator.EFCore;

/// <summary>EF model annotation keys for cache invalidation mapping.</summary>
internal static class CacheOrchestratorEfAnnotations
{
    public const string Domain = "CacheOrchestrator:Domain";
    public const string EntityKind = "CacheOrchestrator:EntityKind";
}
