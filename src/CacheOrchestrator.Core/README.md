# CacheOrchestrator.Core

Http-free core library for CacheOrchestrator: domain configuration, `ICacheOrchestrator`, `CacheDomainContext` (host-supplied domain binding for libraries), invalidation, and the cluster command model.

Composition examples: [docs/packages.md](../../docs/packages.md).

For ASP.NET Core Output Cache / client headers / Admin API, use **CacheOrchestrator.AspNetCore** (or the meta package **CacheOrchestrator**).
For FusionCache as the data-cache provider, use **CacheOrchestrator.FusionCache**.
