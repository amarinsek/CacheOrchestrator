using CacheOrchestrator.Configuration;
using Microsoft.AspNetCore.Http;

namespace CacheOrchestrator.FusionCache;

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

        if (feature.PendingEntityFootprint is { } previous)
        {
            feature.PendingEntityFootprint = previous.Merge(footprint);
        }
        else
        {
            feature.PendingEntityFootprint = footprint;
        }
    }
}