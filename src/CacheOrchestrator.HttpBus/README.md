# CacheOrchestrator.HttpBus

[**CacheOrchestrator**](https://github.com/amarinsek/CacheOrchestrator) is a multi-tier cache coordination and synchronized invalidation library for .NET.

This package is the HTTP **cluster command bus**: it delivers invalidate, Version, and settings patches to every configured peer. Use it when you run more than one instance and need those **commands** everywhere (it does not share Redis cache payloads by itself).

## Install

```bash
dotnet add package CacheOrchestrator.HttpBus --prerelease
```

## Configuration

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

`Membership` may also be `ServiceDiscovery`. Peers authenticate `POST …/cluster/apply` with `X-Cache-Admin-Key` (`Cache:Cluster:Bus:ApiKey`, or `Cache:Admin:ApiKey` if empty). An enabled bus without either key fails startup unless `AllowUnauthenticated: true` is explicitly set for an isolated development network. Old or implausibly future-dated commands are rejected.

## Usage

```bash
dotnet add package CacheOrchestrator --prerelease
dotnet add package CacheOrchestrator.HttpBus --prerelease
```

```csharp
builder.Services.AddCacheOrchestrator(builder.Configuration, o => o.AddHttpClusterBus());

var app = builder.Build();
app.UseCacheOrchestrator();
app.MapCacheOrchestratorHttpBus();
```

Invalidate / Version / settings from Admin or `ICacheOrchestratorInvalidator` are then delivered to peers over the bus.

## Documentation

- [Cluster bus](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/reference/cluster-bus.md)
- [Topologies](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/guide/topologies.md)
- [Packages and composition](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/guide/packages.md) · [composition how-to](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/how-to/composition.md)
- [Repository](https://github.com/amarinsek/CacheOrchestrator)

## License

MIT — [LICENSE.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/LICENSE.md)
