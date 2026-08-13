# CacheOrchestrator.Bus

Cluster command bus for [CacheOrchestrator](https://www.nuget.org/packages/CacheOrchestrator/).

Add this package when you run more than one instance and you need an invalidation, a Version change, or a TTL change to take effect on every node. You get an HTTP bus that delivers those commands to the peers you configure.

## Install

```bash
dotnet add package CacheOrchestrator
dotnet add package CacheOrchestrator.Bus
```

## Register

```csharp
builder.Services.AddCacheOrchestrator(builder.Configuration, o =>
{
    o.AddHttpClusterBus();
});

app.MapCacheOrchestratorHttpBus();
```

## Configure

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

`Membership` may also be `ServiceDiscovery` (`Microsoft.Extensions.ServiceDiscovery`). Peers authenticate `POST …/cluster/apply` with `X-Cache-Admin-Key` (`Cache:Cluster:Bus:ApiKey`, or `Cache:Admin:ApiKey` if the bus key is empty).

Guide: [cluster-bus.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/cluster-bus.md). Overview: [GitHub README](https://github.com/amarinsek/CacheOrchestrator#readme).

## License

MIT — [LICENSE.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/LICENSE.md)
