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

        if (http.Items.TryGetValue(CacheOrchestratorKeys.PendingEntityFootprintKey, out object? existing)
            && existing is EntityFootprint previous)
        {
            http.Items[CacheOrchestratorKeys.PendingEntityFootprintKey] = previous.Merge(footprint);
            return;
        }

        http.Items[CacheOrchestratorKeys.PendingEntityFootprintKey] = footprint;
    }
}