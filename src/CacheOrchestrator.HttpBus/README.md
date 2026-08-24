# CacheOrchestrator.HttpBus

HTTP **cluster command bus** for CacheOrchestrator: deliver invalidate, Version, and settings patches to every configured peer.

Add this when you run more than one instance and need those commands (not Redis L2 payloads) on all nodes.

## Install

```bash
dotnet add package CacheOrchestrator
dotnet add package CacheOrchestrator.HttpBus
```

## Quick start

```csharp
builder.Services.AddCacheOrchestrator(builder.Configuration, o => o.AddHttpClusterBus());

var app = builder.Build();
app.UseCacheOrchestrator();
app.MapCacheOrchestratorHttpBus();
```

```json
{
  "Cache": {
    "Namespace": "app1",
    "InstanceId": "app1-a",
    "Cluster": {
      "Bus": {
        "Enabled": true,
        "Membership": "Static",
        "ApiKey": "…",
        "Static": {
          "Instances": [
            { "Id": "app1-a", "Url": "http://10.0.0.1:8080" },
            { "Id": "app1-b", "Url": "http://10.0.0.2:8080" }
          ]
        }
      }
    }
  }
}
```

`Membership` may also be `ServiceDiscovery`. Peers authenticate `POST …/cluster/apply` with `X-Cache-Admin-Key` (`Cache:Cluster:Bus:ApiKey`, or `Cache:Admin:ApiKey` if empty).

## Documentation

- [Packages and composition](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/packages.md)
- [Cluster bus](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/cluster-bus.md)
- [Topologies](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/guide/topologies.md)
- [GitHub README](https://github.com/amarinsek/CacheOrchestrator#readme)

## License

MIT — [LICENSE.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/LICENSE.md)
