using CacheOrchestrator.Configuration;
using CacheOrchestrator.Entity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace CacheOrchestrator.OutputCache;

internal static class CacheResponseTagStaging
{
    private const string HeaderName = "X-CacheOrchestrator-Staged-Tags";
    private static readonly object EnabledKey = new();

    public static void Initialize(HttpContext http)
    {
        http.Items[EnabledKey] = true;
        Update(http);
    }

    public static void Update(HttpContext http)
    {
        if (!http.Items.ContainsKey(EnabledKey))
            return;

        ICacheOrchestratorFeature feature = CacheOrchestratorFeatureAccessor.GetOrCreate(http);
        if (feature.DomainOptions is not { } options)
            return;

        http.Response.Headers[HeaderName] = new StringValues([.. Collect(feature, options.Domain)]);
    }

    public static IReadOnlyList<string>? Take(HttpContext http)
    {
        if (!http.Response.Headers.TryGetValue(HeaderName, out StringValues values))
            return null;

        http.Response.Headers.Remove(HeaderName);
        return values.Where(static value => !string.IsNullOrEmpty(value)).Select(static value => value!).ToArray();
    }

    public static IReadOnlyList<string> Collect(ICacheOrchestratorFeature feature, string domain)
    {
        List<string> tags = [CacheTags.Domain(domain)];
        HashSet<string> seen = new(tags, StringComparer.Ordinal);

        void Add(string tag)
        {
            if (seen.Add(tag))
                tags.Add(tag);
        }

        if (feature.EntityKind is { Length: > 0 } entityKind)
        {
            Add(CacheTags.EntityKind(domain, entityKind));
            if (feature.ResourceId is { Length: > 0 } resourceId)
                Add(CacheTags.Entity(domain, entityKind, resourceId));
        }

        if (feature.PendingEntityFootprint is { } footprint
            && !ReferenceEquals(footprint, EntityFootprint.Empty))
        {
            foreach (string tag in footprint.ToTags(domain))
                Add(tag);
        }

        return tags;
    }
}
