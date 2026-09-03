# CacheOrchestrator Minimal sample

The smallest application that uses CacheOrchestrator. One endpoint, in-memory stores, no extra packages. You get a working miss and then a hit in `X-CacheOrchestrator` before you open a larger sample.

## Run

```bash
dotnet run --project samples/CacheOrchestrator.Minimal
```

In another terminal:

```bash
curl -i http://localhost:5290/hello
curl -i http://localhost:5290/hello
```

The first response waits about 200 ms (`oc=miss`). The second is served from Output Cache (`oc=hit`).

In a browser, open DevTools, enable **Disable cache** on the Network tab, and request the same URL twice. Otherwise the browser’s own cache hides the server hit.

The domain lives in `appsettings.json` (`Cache:Domains:hello`). The endpoint uses `.CacheOutputWithDomain("hello")` and `IDomainDataCache.GetOrSetAsync`.

## Admin API

This sample turns the Admin API on with a development key. `Program.cs` calls `MapCacheOrchestratorAdmin()`.

```bash
curl -i -H "X-CacheOrchestrator-Admin-Key: dev-admin-key" http://localhost:5290/cache-admin/local/health
```

For a multi-instance UI, run [CacheOrchestrator.AdminConsole](../../src/CacheOrchestrator.AdminConsole) and point `AdminConsole:Instances` at this port. See [docs/reference/admin.md](../../docs/reference/admin.md).

## Next

- [Getting started](../../docs/guide/getting-started.md)
- [Guide](../../docs/guide/README.md) — concepts, topologies, operations
- [Playground sample](../CacheOrchestrator.Sample) — TTLs, schedule, Redis, CRUD, Prometheus `/metrics`
- [Documentation index](../../docs/README.md)
