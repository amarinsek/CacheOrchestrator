# Extensibility

> **Reference.** Product overview: [root README](../../README.md). Orientation: [packages](../guide/packages.md). Catalog: [documentation index](../README.md). Storage details: [backends](backends.md).

CacheOrchestrator exposes several extension points, each at a different boundary. Choose the narrowest one that solves the requirement: add one vary dimension with `ICacheVaryContributor`; do not replace the entire Data Cache engine or key generator for it.

This page distinguishes supported application extension points from provider- and host-integration contracts. A type being public does not automatically mean ordinary applications should replace it.

## Table of Contents

- [Extension point map](#extension-point-map)
- [Request identity: `ICacheIdentityContract`](#request-identity-icacheidentitycontract)
- [Domain-template token providers](#domain-template-token-providers)
- [Vary dimensions: `ICacheVaryContributor`](#vary-dimensions-icachevarycontributor)
- [Full HTTP Data Cache key: `IDomainKeyGenerator`](#full-http-data-cache-key-idomainkeygenerator)
- [Invalidation observer: `ICacheInvalidationObserver`](#invalidation-observer-icacheinvalidationobserver)
- [Health probe: `ICacheOrchestratorHealthProbe`](#health-probe-icacheorchestratorhealthprobe)
- [Output Cache store: `IOutputCacheBackendRegistrar`](#output-cache-store-ioutputcachebackendregistrar)
- [FusionCache L2/backplane: `IFusionCacheBackendRegistrar`](#fusioncache-l2backplane-ifusioncachebackendregistrar)
- [Data Cache engine: `IDataCacheProvider`](#data-cache-engine-idatacacheprovider)
- [Provider-specific runtime settings](#provider-specific-runtime-settings)
- [Satellite-package builders](#satellite-package-builders)
- [Cluster contracts](#cluster-contracts)
- [Advanced host integration contracts](#advanced-host-integration-contracts)
- [Public utility types](#public-utility-types)

## Extension point map

| Requirement | Extension point | Typical owner |
|-------------|-----------------|---------------|
| Derive endpoint identity from a request | `ICacheIdentityContract` | Application |
| Supply a custom domain-template token | `customProviders` on `CacheOutputWithDomainTemplate` | Application |
| Add a tenant, claim, header, or other vary dimension | `ICacheVaryContributor` | Application |
| Replace the complete HTTP Data Cache key algorithm | `IDomainKeyGenerator` | Application, advanced |
| Audit invalidations or send a webhook | `ICacheInvalidationObserver` | Application |
| Add a readiness probe | `ICacheOrchestratorHealthProbe` | Application or provider |
| Add an Output Cache store | `IOutputCacheBackendRegistrar` | Backend package |
| Add FusionCache L2 or a backplane | `IFusionCacheBackendRegistrar` | Backend package |
| Add a complete Data Cache engine | `IDataCacheProvider` | Provider package |
| Add provider-specific runtime settings | `DomainSettingCatalog` + `IDomainSettingsPatchContributor` | Provider package |
| Add a satellite package during host composition | `ICacheOrchestratorServiceBuilder` / `ICacheOrchestratorBuilder` | Integration package |
| Replace peer discovery | `IClusterMembership` | Infrastructure integration |
| Replace cluster command transport | `IClusterCommandBus` | Infrastructure integration |
| Apply received commands locally | `IClusterCommandHandler` | Transport package or host integration |
| Replace stable process identity | `IInstanceIdProvider` | Host integration |

## Request identity: `ICacheIdentityContract`

Use a named identity contract when an endpoint's cache identity is not simply URL-shaped. The contract receives `CacheIdentityContext` and returns stable `CacheIdentityMaterial`; returning `null` bypasses caching for that request.

```csharp
public sealed class ProductSearchIdentity : ICacheIdentityContract
{
    public string Name => "product-search-v1";

    public ValueTask<CacheIdentityMaterial?> BuildAsync(
        CacheIdentityContext context,
        CancellationToken cancellationToken)
    {
        string category = context.HttpContext.Request.Query["category"]
            .ToString()
            .Trim()
            .ToLowerInvariant();

        return ValueTask.FromResult<CacheIdentityMaterial?>(
            new CacheIdentityMaterial([
                new KeyValuePair<string, string>("category", category)
            ]));
    }
}

builder.Services.AddCacheIdentityContract<ProductSearchIdentity>();
```

Bind it with `.WithCacheIdentity(["POST"], "product-search-v1")` or `[CacheIdentity]`. Contracts are singleton services, so they must be thread-safe and must not retain request state. Full contract and binding rules: [endpoint cache identity](cache-identity.md).

## Domain-template token providers

`CacheOutputWithDomainTemplate` accepts an optional dictionary of `{custom:key}` providers. This is the narrow extension point for deriving a configured domain from trusted request state without replacing policy resolution. Providers receive `HttpContext`, are captured when the template is compiled, and return one string segment. See [Output Cache](output-cache.md#minimal-apis) for the complete example and token list.

## Vary dimensions: `ICacheVaryContributor`

Use a contributor for a small request dimension shared by Output Cache and HTTP Data Cache key generation.

| Contract | Purpose |
|----------|---------|
| `ICacheVaryContributor.Order` | Lower values execute first; application contributors normally use 100 or greater |
| `CacheVaryContext.HttpContext` | Current request |
| `CacheVaryContext.Options` | Resolved domain snapshot |
| `CacheVaryContext.Surface` | `OutputCache` or `Fusion`; lets a contributor target one surface deliberately |
| `ICacheVaryBuilder.AddHeader` | Add a request header; sensitive headers are hashed |
| `ICacheVaryBuilder.AddValue` | Add non-secret stable material |
| `ICacheVaryBuilder.AddHashedValue` | Hash secret or high-entropy material before keying |

```csharp
public sealed class TenantVaryContributor : ICacheVaryContributor
{
    public int Order => 100;

    public void Contribute(CacheVaryContext context, ICacheVaryBuilder builder)
    {
        string? tenantId = context.HttpContext.User.FindFirst("tenant_id")?.Value;
        if (!string.IsNullOrWhiteSpace(tenantId))
            builder.AddValue("tenant", tenantId);
    }
}

builder.Services.AddSingleton<ICacheVaryContributor, TenantVaryContributor>();
```

Do not pass bearer tokens, cookies, API keys, or other secrets to `AddValue`. See [domain vary dimensions](vary.md#custom-vary-icachevarycontributor).

## Full HTTP Data Cache key: `IDomainKeyGenerator`

Replace `IDomainKeyGenerator` only when a contributor cannot express the required key shape. The generator receives a `DomainCacheKeyContext` containing the resolved `DomainHttpCacheOptions`, `HttpContext`, and `DomainCacheKeyShape`. Respect `Url` by ignoring request entity identity, respect `Entity` by using it when available, and preserve the `Automatic` behavior for direct callers. The result must be deterministic, compact, and non-secret.

Register before `AddCacheOrchestratorAspNetCore` so its `TryAddSingleton` keeps the custom implementation, or replace the registration afterwards.

```csharp
builder.Services.AddSingleton<IDomainKeyGenerator, TenantKeyGenerator>();
builder.Services.AddCacheOrchestratorAspNetCore(builder.Configuration);
```

Prefer composing `DefaultDomainKeyGenerator(CacheVaryMaterializer)` so built-in Accept, authentication, query, and contributor material remains present. Details: [Data Cache custom key generator](data-cache.md#custom-key-generator).

## Invalidation observer: `ICacheInvalidationObserver`

Observers receive before/after callbacks for audit, metrics, or webhooks. They execute in DI registration order on the process applying the invalidation. An observer exception is logged and does not fail the store operation.

Each public invalidator call produces one observer pair. A multi-domain call uses `CacheInvalidationKind.Domains`; its after result exposes ordered per-domain `Parts`, including partial and cluster-publish failures.

Observers do not distribute invalidations. Use Redis backplane, HttpBus, or Admin Console App fan-out for peer processes. See [invalidation observers](invalidation.md#observers-audit--webhooks).

## Health probe: `ICacheOrchestratorHealthProbe`

A probe has a stable `Name` and a `ProbeAsync` method that completes when the dependency is usable and throws when it is not.

```csharp
public sealed class SearchClusterCacheProbe : ICacheOrchestratorHealthProbe
{
    public string Name => "search-cluster";

    public Task ProbeAsync(CancellationToken cancellationToken = default) =>
        CheckSearchClusterAsync(cancellationToken);
}

builder.Services.AddSingleton<ICacheOrchestratorHealthProbe, SearchClusterCacheProbe>();
```

`AddCacheOrchestrator` on `IHealthChecksBuilder` runs all registered probes. Provider registrars add probes through `context.Services` in their main registration method. See [observability](observability.md#health-checks).

## Output Cache store: `IOutputCacheBackendRegistrar`

`IOutputCacheBackendRegistrar` configures **only the Output Cache storage surface**. Its `Name` matches `Cache:OutputCache:Provider`.

| Member | Purpose |
|--------|---------|
| `Name` | Provider name used in configuration |
| `RegisterOutputCache(OutputCacheRegistrationContext)` | Configure `OutputCacheOptions` and register the store |

Use `context.Configure(...)` instead of calling `AddOutputCache` yourself. Use `context.RegisterStore(...)` when an adapter must register after the shared Output Cache services. Backend-specific configuration is available at `context.BackendSection` under `{root}:OutputCache:{Provider}`.
`context.OutputCacheNamespace` is the effective namespace already resolved by `CacheOrchestrator.AspNetCore`; a store adapter should use it instead of binding root options itself.

Register it through `ICacheOrchestratorBuilder.AddOutputCacheBackend`:

```csharp
builder.Services.AddCacheOrchestratorAspNetCore(
    builder.Configuration,
    options => options.AddOutputCacheBackend(new MyOutputCacheRegistrar()));
```

## FusionCache L2/backplane: `IFusionCacheBackendRegistrar`

`IFusionCacheBackendRegistrar` does not replace the Data Cache engine. It attaches distributed storage and optionally a backplane to each named FusionCache instance whose `Provider` matches the registrar `Name`.

`FusionCacheRegistrationContext` supplies:

- the named `InstanceName` and `InstanceOptions`;
- `FusionBuilder` with memory cache and serializer already configured;
- effective `DistributedResilience` settings;
- backend configuration at `{root}:DataCacheInstances:{instance}:{Provider}`;
- `Services`, `Configuration`, and root options.

Register a keyed `IDistributedCache` for each instance. A single unkeyed registration lets the last named instance overwrite the others. The complete SQL Server example is in [backends](backends.md#example-fusion-l2-on-sql-server).

## Data Cache engine: `IDataCacheProvider`

Implement `IDataCacheProvider` only when adding a complete engine alongside FusionCache and HybridCache.

| Member | Contract |
|--------|----------|
| `Name` | Stable provider name used in diagnostics |
| `GetOrCreateAsync<T>` | Read or produce a value and return `DataCacheProviderResult<T>` with the actual outcome |
| `SetAsync<T>` | Overwrite the value and final tags after footprint expansion |
| `InvalidateAsync` | Remove all requested tags from one named instance, or from all instances when `InstanceName` is `null` |

`DataCacheProviderRequest` contains:

| Property | Meaning |
|----------|---------|
| `Key` | Complete physical key; domain and Version are already included |
| `InstanceName` | Selected `DataCacheInstances` name |
| `Tags` | Domain, entity, entity-kind, and custom tags |
| `DomainOptions` | Resolved portable policy snapshot |

`DataCacheProviderResult<T>.Outcome` must be `Materialized` only when the returned value came from this call's successfully completed factory invocation, `Cached` for a fresh hit, and `Stale` whenever fail-safe or a background refresh returns an expired value. Never return `Unknown`; it is the invalid default-struct state and the orchestrator rejects it. The orchestrator uses this distinction both for HTTP disposition and to decide whether a factory-expanded entity footprint may replace stored tags.

The provider must be thread-safe and preserve generic values, cancellation, null/negative-cache payloads, named-instance isolation, configured namespaces, and tag invalidation. It must not rebuild HTTP vary material. `SetAsync` is an implementor-facing overwrite used only after a successfully materialized factory expands the early footprint; providers must replace both value and tag metadata. A provider that cannot support named instances should reject non-default `DataCacheInstances` during options validation instead of silently sharing one store.

`DataCacheInvalidationRequest` deliberately groups `Tags` and optional `InstanceName` into one operation. New optional provider features should be introduced as separate capability interfaces instead of growing `IDataCacheProvider` with unrelated members.

Two such optional interfaces are built in:

| Interface | Purpose |
|-----------|---------|
| `IDataCacheProviderCapabilities` | Publishes a stable `DataCacheProviderCapabilities` descriptor for diagnostics and management clients |
| `IDataCacheBatchInvalidator` | Accepts invalidation requests for several named instances in one call, so a provider can bound or combine backend work |

Implementing neither interface remains valid. CacheOrchestrator reports all optional capabilities as unsupported and falls back to calling `InvalidateAsync` once per named instance. A batch implementation must complete only after every request has completed, propagate cancellation, and throw when any requested invalidation fails.

The descriptor says what the registered provider implementation can support, not whether every optional backend is configured in the current process:

| Capability | FusionCache provider | HybridCache provider |
|------------|----------------------|----------------------|
| Named instances | Yes | No |
| Fail-safe / stale fallback | Yes | No |
| Eager refresh | Yes | No |
| Backplane integration | Yes | No |
| Entry-size limit | Yes | No |
| Batch invalidation | Yes | Yes |

Provider name and capabilities are exposed in health-check data and the Admin API health response. Inspect effective configuration and provider health probes as well when you need to know whether a supported distributed store or backplane is actually active.

Register exactly one provider. Core registration uses `TryAddSingleton`, so an application-owned provider can be registered first:

```csharp
builder.Services.AddSingleton<IDataCacheProvider, MyDataCacheProvider>();
builder.Services.AddCacheOrchestratorCore(builder.Configuration);
```

An HTTP host can use `AddCacheOrchestratorAspNetCore` in the second line; it includes the Core registration and adds the HTTP surfaces.

Do not then call `AddCacheOrchestratorFusionCache` or `AddCacheOrchestratorHybridCache`; both intentionally replace the current `IDataCacheProvider`.

`AddCacheOrchestratorCore` registers the default orchestrator, options, invalidator, and HTTP-free cluster defaults. A standalone worker combines it with one provider registration. A reusable class library can reference only `CacheOrchestrator.Core` and let its host own both registrations.

## Provider-specific runtime settings

A package can add settings to the Admin runtime catalog:

1. Define a settings type whose properties use `DomainSettingAttribute`.
2. Call `DomainSettingCatalog.RegisterSection(type, idPrefix, propertyPrefix)` during registration.
3. Register an `IDomainSettingsPatchContributor` that owns and applies those setting IDs.

FusionCache uses this mechanism for `fusionCache.hardTtlSeconds`, fail-safe, jitter, timeouts, and background-operation flags.

`RuntimeOverlay = true` controls whether Admin PATCH accepts a setting. Contributors must validate values before mutating their process-local store and must treat a patch as a sparse merge. Distributed Admin changes carry the same setting dictionary through `SettingsPatchCommand`.

## Satellite-package builders

`ICacheOrchestratorServiceBuilder` exposes `Services` and `Configuration` to packages that add host services without depending on Output Cache registration. `ICacheOrchestratorBuilder` extends it with `AddOutputCacheBackend` and `ConfigureOutputCache` for ASP.NET Core composition.

Integration packages should add focused extension methods on the narrowest builder they require. The EF Core invalidation and HttpBus packages follow this pattern. Applications normally use the shipped extension methods rather than implement either builder.

## Cluster contracts

| Contract | Purpose | Default |
|----------|---------|---------|
| `IClusterMembership` | Resolve peer `ClusterPeer` records; may include self | `NullClusterMembership` |
| `IClusterCommandBus` | Publish a `ClusterCommand` and report per-peer results | `NullClusterCommandBus` |
| `IClusterCommandHandler` | Apply a received command locally without re-publishing | Built-in handler |
| `IInstanceIdProvider` | Stable local process id for Admin and anti-echo | `Cache:InstanceId`, then machine name |

Custom transports should publish commands without cache payloads, preserve `CommandId`, namespace, origin, timestamp, and correlation id, and return individual peer failures in `ClusterPublishResult`. A received command must be applied through `IClusterCommandHandler.ApplyLocalAsync` so remote scope suppresses echo. Command records are semantic Core contracts, not a prescribed serialized form; each transport owns and versions its wire protocol.

Use the shipped `CacheOrchestrator.HttpBus` unless another transport or discovery system is a firm infrastructure requirement. See [cluster command bus](cluster-bus.md).

## Advanced host integration contracts

These contracts are public for host and satellite-package integration. Ordinary application code should consume their higher-level APIs instead of replacing them.

| Contract | Purpose | Preferred application API |
|----------|---------|---------------------------|
| `IDomainCacheOptionsProvider` | Resolve HTTP-free Core snapshots and process cache | Inject `DomainCacheOptions` indirectly through `ICacheOrchestrator` |
| `IDomainRuntimeOverrideStore` | Hold process-local Version and portable settings overlays | Admin Version/settings endpoints |
| `IRequestDomainCacheOptions` | Resolve and attach `DomainHttpCacheOptions` to `HttpContext` | Endpoint metadata or explicit `IDomainDataCache` domain overload |
| `ICacheOrchestratorFeature` | Per-request options, identity, disposition, and staged footprint | `HttpContext.GetDomainCacheOptions()` and Data Cache helpers |
| `IHttpCacheInvalidationSink` | HTTP-free bridge from Core invalidation to Output Cache | `ICacheOrchestratorInvalidator` |
| `ICacheOrchestratorManagement` | Management API — transport-independent queries and operations | Inject from Core; expose through a host-appropriate secured adapter (e.g. Admin API) |
| `IAdminDomainConfigProvider` | Enrich the Core domain view with host-specific policy | Built-in Core Data Cache view or ASP.NET Core HTTP view |
| `IAdminStatsCollector` / `IAdminEndpointCatalog` | Management instrumentation and host resource discovery | Admin API and metrics |
| `IFusionDomainSettingsProvider` / `IFusionDomainRuntimeOverrideStore` | `CacheOrchestrator.FusionCache` policy and overlay integration | Domain configuration and Admin PATCH |

Replacing one of these contracts means taking responsibility for its caching, normalization, concurrency, reload, and lifecycle semantics.

`HttpContext`, `ICacheOrchestratorFeature`, and internal request identity state are request-scoped. Custom contributors, identity contracts, and key generators must not retain them beyond the request or access them concurrently from arbitrary background work.

## Public utility types

The packages also expose pure helpers and DTOs. `CacheOrchestrator.Core` owns HTTP-free types such as `DomainName`, `CacheTags`, `FactoryResultSize`, Admin DTOs, and cluster command records. `CacheOrchestrator.AspNetCore` owns `ClientCacheHeaderGenerator`, `CacheETagFactory`, and `XCacheHeaderFormatter`. They are documented on the reference page for the behavior they represent and in NuGet XML documentation. They are not service replacement points unless listed above.

## Related

- [Core API](core-api.md)
- [Backends](backends.md)
- [Data Cache](data-cache.md)
- [Endpoint cache identity](cache-identity.md)
- [Domain vary dimensions](vary.md)
- [Invalidation](invalidation.md)
- [Observability](observability.md)
- [Cluster command bus](cluster-bus.md)
