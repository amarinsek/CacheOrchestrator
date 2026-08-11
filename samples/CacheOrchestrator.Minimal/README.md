# CacheOrchestrator Minimal sample

[CacheOrchestrator](../..) is domain-based caching for ASP.NET Core that orchestrates Output Cache, FusionCache, and client Cache-Control under the same model.

This sample: one endpoint, InMemory only, no Redis. **Goal:** see a cache **MISS** then **HIT** in under a minute.

## Run

1. Start the application:

```bash
dotnet run --project samples/CacheOrchestrator.Minimal
```

2. In a second terminal, execute these requests:

```bash
curl -i http://localhost:5290/hello
curl -i http://localhost:5290/hello
```

> **Optional: Using a browser.** If you prefer using a browser instead of curl, you must open your DevTools (F12) and check **Disable cache** on the Network tab first. Otherwise, the browser will serve its own local cache and you won't see the second server-side hit.


---

## What to look for

| Request | Typical `X-Cache` | Feel |
| --- | --- | --- |
| 1st | `output=miss` (and often `data=miss`) | ~200 ms delay (simulated work) |
| 2nd | `output=hit` | Instant full response from Output Cache |

---

## What this shows

* Domain rules defined in `appsettings.json` (`Cache:Domains:hello`)
* Service registration and middleware wiring (`AddCacheOrchestrator` + `UseCacheOrchestrator`)
* Endpoint decoration and data fetching (`.CacheOutputWithDomain("hello")` + `IDomainFusionCache.GetOrSetAsync`)

---

## Next

| Next step | Where |
| --- | --- |
| Day-1 walkthrough | [docs/getting-started.md](https://www.google.com/search?q=../../docs/getting-started.md) |
| Interactive playground (TTL, schedule, Redis, CRUD) | [../CacheOrchestrator.Sample](https://www.google.com/search?q=../CacheOrchestrator.Sample) |
| Full docs | [docs/README.md](https://www.google.com/search?q=../../docs/README.md) |
