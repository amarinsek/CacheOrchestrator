using CacheOrchestrator.Configuration;
using CacheOrchestrator.Entity;
using Microsoft.AspNetCore.Http;

namespace CacheOrchestrator.DataCache;

/// <summary>
/// Stages an <see cref="EntityFootprint"/> on the request for Output Cache late tagging.
/// </summary>
internal static class EntityFootprintStaging
{
    public static void Stage(HttpContext http, EntityFootprint footprint)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (footprint is null || ReferenceEquals(footprint, EntityFootprint.Empty))
            return;

        ICacheOrchestratorFeature feature = CacheOrchestratorFeatureAccessor.GetOrCreate(http);

        feature.PendingEntityFootprint = feature.PendingEntityFootprint is { } previous ? previous.Merge(footprint) : footprint;
    }
}
