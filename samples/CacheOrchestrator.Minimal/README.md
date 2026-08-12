# CacheOrchestrator Minimal sample

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

## Local Admin (this sample)

`appsettings.json` enables Local Admin with a **dev** API key (`Cache:Admin:Enabled`, `Cache:InstanceId`).  
Map: `MapCacheOrchestratorAdmin()` in `Program.cs`.

```bash
curl -i -H "X-Cache-Admin-Key: dev-admin-key" http://localhost:5290/cache-admin/local/health
```

Multi-instance UI: run [CacheOrchestrator.Admin](../../src/CacheOrchestrator.Admin) and point `CacheAdmin:Instances` at this port — [docs/admin.md](../../docs/admin.md).

## Next

| Next step | Where |
| --- | --- |
| Day-1 walkthrough | [docs/getting-started.md](../../docs/getting-started.md) |
| Interactive playground (TTL, schedule, Redis, CRUD) | [../CacheOrchestrator.Sample](../CacheOrchestrator.Sample) |
| Local Admin / Admin App | [docs/admin.md](../../docs/admin.md) |
| Full docs | [docs/README.md](../../docs/README.md) |
