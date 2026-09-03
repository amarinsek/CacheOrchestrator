# CacheOrchestrator.HttpBus

[**CacheOrchestrator**](https://github.com/CacheOrchestrator/CacheOrchestrator) is a multi-tier cache coordination and synchronized invalidation library for .NET.

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

`Membership` may also be `ServiceDiscovery`. Peers authenticate `POST …/cluster/apply` with `X-CacheOrchestrator-Admin-Key` (`Cache:Cluster:Bus:ApiKey`, or `Cache:Admin:ApiKey` if empty). An enabled bus without either key fails startup unless `AllowUnauthenticated: true` is explicitly set for an isolated development network. Old or implausibly future-dated commands are rejected.

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

- [README](https://github.com/CacheOrchestrator/CacheOrchestrator/blob/main/README.md)
- [Documentation index](https://github.com/CacheOrchestrator/CacheOrchestrator/blob/main/docs/README.md)
- [Repository](https://github.com/CacheOrchestrator/CacheOrchestrator)

## License

MIT — [LICENSE.md](https://github.com/CacheOrchestrator/CacheOrchestrator/blob/main/LICENSE.md)
