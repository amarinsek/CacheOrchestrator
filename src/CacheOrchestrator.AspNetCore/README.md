# CacheOrchestrator.AspNetCore

ASP.NET Core host integration for CacheOrchestrator: Output Cache policies, client Cache-Control, Admin API, vary materialization, and HTTP-aware domain data-cache helpers (`IDomainFusionCache`).

Depends on **CacheOrchestrator.Core** and **CacheOrchestrator.FusionCache** (default `IDataCacheProvider`).
For a single package reference that pulls these together, use the meta package **CacheOrchestrator**. Hybrid: add **CacheOrchestrator.HybridCache** and call `AddCacheOrchestratorHybridCache()`.
