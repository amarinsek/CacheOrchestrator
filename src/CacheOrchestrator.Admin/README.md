# CacheOrchestrator.Admin

![Admin App overview](../../docs/assets/admin-overview.png)

An operator host for CacheOrchestrator. You get one place to see hit rates, domain settings, and health across instances, and to invalidate or adjust Version and TTL without opening each process.

The **Admin API** on the application (`Cache:Admin:Enabled`, `MapCacheOrchestratorAdmin`) is what this host calls. This project is the UI and the fan-out. It targets **.NET 10**; the applications themselves may be .NET 8 or .NET 10.

Full guide: [docs/admin.md](../../docs/admin.md). Hint rules: [docs/admin-hints.md](../../docs/admin-hints.md).

## Enable the Admin API on each instance

```json
"Cache": {
  "InstanceId": "app-1",
  "Admin": {
    "Enabled": true,
    "ApiKey": "dev-admin-key"
  }
}
```

```csharp
app.MapCacheOrchestratorAdmin();
```

## Configure this host

```json
{
  "CacheAdmin": {
    "ApiKey": "dev-admin-key",
    "RequestTimeoutMs": 3000,
    "Parallelism": 8,
    "LocalPathPrefix": "/cache-admin/local",
    "Instances": [
      { "id": "app-1", "url": "http://localhost:5290" },
      { "id": "app-2", "url": "http://localhost:5291" }
    ]
  }
}
```

- **ApiKey** is sent to each instance as `X-Cache-Admin-Key`. It must match `Cache:Admin:ApiKey`.
- **Instances[].url** is the application base URL only.
- **LocalPathPrefix** must match `Cache:Admin:RoutePrefix` on the instances.

Keep production keys in a secret store. The sample value `dev-admin-key` is for local work. This host has no built-in login; put a VPN or SSO proxy in front of it. Invalidate, Version, and TTL operations change live cache state. Checklist: [docs/admin.md — Security](../../docs/admin.md#security).

## Run

```bash
dotnet run --project src/CacheOrchestrator.Admin
```

- http://localhost:5188/ — UI
- http://localhost:5188/health — process health
- http://localhost:5188/scalar/v1 — OpenAPI, Development only

A convenient pair is the Minimal sample with the Admin API enabled, and `Instances` pointed at that port.

Pages (hash routes): Overview, Instances, Domains, Endpoints, Hints, Operations. Operations use HTTP fan-out or the cluster bus (`distribute`), whichever the instances support.

Further detail: [docs/admin.md](../../docs/admin.md) · [docs/cluster-bus.md](../../docs/cluster-bus.md).
