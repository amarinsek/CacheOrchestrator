# CacheOrchestrator.FusionCache

Registers ZiggyCreatures FusionCache as the `IDataCacheProvider` for CacheOrchestrator (data cache / DC). Owns nested JSON `FusionCache` knobs; portable TTL lives under `DataCache`.

Use with **CacheOrchestrator.AspNetCore** (or the meta package **CacheOrchestrator**) for full HTTP + domain orchestration. Named engines: root `DataCacheInstances`.
