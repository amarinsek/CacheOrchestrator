# Comparison

This page compares the usual way of wiring ASP.NET Core Output Cache and FusionCache yourself with the same work done through CacheOrchestrator.

The same endpoint is written both ways, without and with CacheOrchestrator.

### Without CacheOrchestrator

```csharp
builder.Services.AddOutputCache(options =>
{
    options.AddPolicy("osm-tiles", policy => policy
        .Expire(TimeSpan.FromDays(7))
        .Tag("domain:osm-tiles")
        .SetVaryByHost(true)
        .SetVaryByQuery("format")
        .SetVaryByValue("data-version", _ => "2026-08"));
});

builder.Services.AddFusionCache()
    .WithDefaultEntryOptions(e =>
    {
        e.Duration = TimeSpan.FromDays(30);
        e.SetFailSafe(true, TimeSpan.FromDays(90));
        e.JitterMaxDuration = TimeSpan.FromSeconds(30);
        e.FactorySoftTimeout = TimeSpan.FromMilliseconds(150);
        e.FactoryHardTimeout = TimeSpan.FromSeconds(2);
    });

app.UseOutputCache();

app.MapGet("/tiles/{z}/{x}/{y}", async (
    HttpContext http,
    IFusionCache cache,
    int z, int x, int y,
    CancellationToken cancellationToken) =>
{
    if (http.User.Identity?.IsAuthenticated == true
        || !string.IsNullOrEmpty(http.Request.Headers.Authorization))
    {
        http.Response.Headers.CacheControl = "no-store";
        var fresh = await LoadTileAsync(z, x, y, cancellationToken);
        return Results.Bytes(fresh, "image/png");
    }

    var key = $"osm-tiles:2026-08:{z}:{x}:{y}";
    var tile = await cache.GetOrSetAsync(
        key,
        ct => LoadTileAsync(z, x, y, ct),
        tags: ["domain:osm-tiles"],
        token: cancellationToken);

    var cutover = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    var maxAge = ClientMaxAge(
        utcNow: DateTimeOffset.UtcNow,
        scheduledUpdateUtc: cutover,
        calmSeconds: 2_592_000,
        floorSeconds: 900);

    http.Response.Headers.CacheControl = $"public, max-age={maxAge}, must-revalidate";
    http.Response.Headers.ETag = "W/\"2026-08\"";

    return Results.Bytes(tile, "image/png");
})
.CacheOutput("osm-tiles");

static int ClientMaxAge(
    DateTimeOffset utcNow,
    DateTimeOffset scheduledUpdateUtc,
    int calmSeconds,
    int floorSeconds)
{
    var secondsLeft = (scheduledUpdateUtc - utcNow).TotalSeconds;
    if (secondsLeft <= 0)
        return floorSeconds;
    if (secondsLeft >= calmSeconds)
        return calmSeconds;

    var t = Math.Clamp(secondsLeft, floorSeconds, calmSeconds);
    return (int)Math.Round(
        floorSeconds + (calmSeconds - floorSeconds) * (t - floorSeconds) / (calmSeconds - floorSeconds));
}
```

Invalidation without CacheOrchestrator:

```csharp
await fusionCache.RemoveByTagAsync("domain:osm-tiles", cancellationToken);
await outputCache.EvictByTagAsync("domain:osm-tiles", cancellationToken);
```

A second domain (catalog, PII, live positions) means another named Output Cache policy, another Fusion entry-options block, another key scheme, and another copy of the header logic.

### With CacheOrchestrator

```json
"Domains": {
  "osm-tiles": {
    "Version": "2026-08",
    "ETagMode": "Version",
    "ClientCacheability": "Public",
    "ClientTtlSeconds": 2592000,
    "ClientTtlMinSeconds": 900,
    "ScheduledUpdateUtc": "2026-09-01T00:00:00Z",
    "ClientMustRevalidateNearUpdate": true,
    "OutputCacheTtlSeconds": 604800,
    "FusionCacheSoftTtlSeconds": 2592000,
    "FusionCacheHardTtlSeconds": 5184000,
    "FusionCacheFailSafeSeconds": 7776000
  }
}
```

```csharp
builder.Services.AddCacheOrchestrator(builder.Configuration);
app.UseCacheOrchestrator();

app.MapGet("/tiles/{z}/{x}/{y}", async (
    HttpContext http,
    IDomainFusionCache cache,
    int z, int x, int y,
    CancellationToken cancellationToken) =>
{
    var tile = await cache.GetOrSetAsync(http, ct => LoadTileAsync(z, x, y, ct), cancellationToken);
    return Results.Bytes(tile, "image/png");
})
.CacheOutputWithDomain("osm-tiles");
```

Invalidation with CacheOrchestrator:

```csharp
await invalidator.InvalidateDomainAsync("osm-tiles", cancellationToken);
```

A second domain is another entry under `Domains` and `.CacheOutputWithDomain("…")` on the route. Auth bypass, tags, Version, Client Cache Schedule, and `X-Cache` stay in the library.

| Concern | Manual | CacheOrchestrator |
|---------|--------|-------------------|
| Per-domain TTLs | Many named policies or magic numbers | One domain entry in config |
| Client `max-age` near cutover | Hand-written or forgotten | **Client Cache Schedule** |
| Fusion + OC same domain | Duplicate config | Shared `DomainCacheOptions` |
| Auth bypass / per-user vary | Easy to get wrong | Defaults + explicit flags |
| Tags `domain:` / `entity:` | Manual string conventions | Built-in |
| Multi Redis for PII vs catalog | Easy to get L2 wrong | Named instances + keyed L2 |
| `X-Cache` diagnostics | You write the header | Built-in |

## Smaller cases

One or two endpoints, Output Cache on Redis alone, or FusionCache in a worker with no HTTP, can look simpler if you wire the platform APIs yourself. Even then CacheOrchestrator is worth taking:

- **Configuration** — TTLs, Version, and client headers live in `appsettings`, not in the handler.
- **Clean endpoints** — the route loads data; the domain owns caching.
- **Topology** — InMemory to Redis, or a second Fusion instance, is a provider change, not a rewrite.
- **Room to grow** — a second domain, Client Cache Schedule, entity invalidation, or the cluster bus sit on the same model when you need them.

Custom storage (SQL, Memcached, …) is a registrar you add. See [backends.md](backends.md) for a Fusion L2 example on SQL Server.

## Related

- [faq.md](faq.md)
- [architecture.md](architecture.md)
- [backends.md](backends.md)
