# CacheOrchestrator Minimal sample

[CacheOrchestrator](../..) is domain-based caching for ASP.NET Core that orchestrates Output Cache, FusionCache, and client Cache-Control under the same model.

This sample: one endpoint, InMemory only, no Redis. **Goal:** see a cache **MISS** then **HIT** in under a minute.

## Run

```bash
dotnet run --project samples/CacheOrchestrator.Minimal
```

### CI smoke (automated)

GitHub Actions runs the same miss → hit check after every build:

```bash
# After: dotnet build samples/CacheOrchestrator.Minimal -c Release
bash samples/CacheOrchestrator.Minimal/smoke.sh
```

This starts the app, calls `/hello` twice, asserts `X-Cache` contains a miss then `output=hit`, then stops the process.

Then either:

- open http://localhost:5290/ and follow the on-page steps, or  
- in a second terminal:

```bash
curl -i http://localhost:5290/hello
curl -i http://localhost:5290/hello
```

### What to look for

| Request | Typical `X-Cache` | Feel |
|---------|-------------------|------|
| 1st | `output=miss` (and often `data=miss`) | ~200 ms delay (simulated work) |
| 2nd | `output=hit` | Instant full response from Output Cache |

Use **curl** or browser DevTools with **Disable cache** — otherwise the browser may serve its own cache and you will not hit the server.

## What this shows

- Domain rules in `appsettings.json` (`Cache:Domains:hello`)
- `AddCacheOrchestrator` + `UseCacheOrchestrator`
- `.CacheOutputWithDomain("hello")` + `IDomainFusionCache.GetOrSetAsync`

## Next

| Next step | Where |
|-----------|--------|
| Day-1 walkthrough | [docs/getting-started.md](../../docs/getting-started.md) |
| Interactive playground (TTL, schedule, Redis, CRUD) | [../CacheOrchestrator.Sample](../CacheOrchestrator.Sample) |
| Full docs | [docs/README.md](../../docs/README.md) |
