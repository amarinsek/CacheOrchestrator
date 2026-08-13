# CacheOrchestrator Minimal sample

The smallest application that uses CacheOrchestrator. One endpoint, in-memory stores, no extra packages. You get a working miss and then a hit in `X-Cache` before you open a larger sample.

## Run

```bash
dotnet run --project samples/CacheOrchestrator.Minimal
```

In another terminal:

```bash
curl -i http://localhost:5290/hello
curl -i http://localhost:5290/hello
```

The first response waits about 200 ms (`output=miss`). The second is served from Output Cache (`output=hit`).

In a browser, open DevTools, enable **Disable cache** on the Network tab, and request the same URL twice. Otherwise the browser’s own cache hides the server hit.

The domain lives in `appsettings.json` (`Cache:Domains:hello`). The endpoint uses `.CacheOutputWithDomain("hello")` and `IDomainFusionCache.GetOrSetAsync`.

## Admin API

This sample turns the Admin API on with a development key. `Program.cs` calls `MapCacheOrchestratorAdmin()`.

```bash
curl -i -H "X-Cache-Admin-Key: dev-admin-key" http://localhost:5290/cache-admin/local/health
```

For a multi-instance UI, run [CacheOrchestrator.Admin](../../src/CacheOrchestrator.Admin) and point `CacheAdmin:Instances` at this port. See [docs/admin.md](../../docs/admin.md).

## Next

- [Getting started](../../docs/getting-started.md)
- [Playground sample](../CacheOrchestrator.Sample) — TTLs, schedule, Redis, CRUD
- [Documentation index](../../docs/README.md)
