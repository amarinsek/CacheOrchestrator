using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace CacheOrchestrator.FusionCache;

/// <summary>
/// Generic extensions for <see cref="IDomainFusionCache"/> to avoid manual <c>.ToString()</c> calls.
/// </summary>
public static class IDomainFusionCacheExtensions
{
    /// <summary>
    /// Sets entity identity on the request for Fusion-only endpoints using a generic resource id.
    /// </summary>
    public static void SetEntityIdentity<TId>(
        this IDomainFusionCache cache,
        HttpContext http,
        string entityKind,
        TId resourceId) where TId : notnull
    {
        ArgumentNullException.ThrowIfNull(cache);
        cache.SetEntityIdentity(http, entityKind, FormatId(resourceId));
    }

    private static string FormatId<TId>(TId id) where TId : notnull
    {
        return id switch
        {
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => id.ToString() ?? string.Empty
        };
    }
}
