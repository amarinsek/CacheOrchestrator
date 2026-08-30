# Core API

> **Reference** — HTTP-free `ICacheOrchestrator`, management, and entity operations.

`CacheOrchestrator.Core` is the HTTP-free application API for class libraries, workers, message handlers, gRPC services, and other hosts that should not depend on ASP.NET Core. The primary abstraction is `ICacheOrchestrator`; a registered `IDataCacheProvider` owns physical storage. Without a provider, startup and health remain valid for hosts that never use Data Cache; the first actual Data Cache operation logs a warning and runs uncached.

The caller supplies a **domain** and a stable **logical key**. CacheOrchestrator resolves the domain policy, adds the domain `Version`, attaches invalidation tags, and delegates the operation to the provider.

```text
application logical key
  → ICacheOrchestrator
      → resolve DomainCacheOptions
      → build co3:{escapedDomain}:{versionHex}:{logicalKey}
      → attach domain/entity tags
      → IDataCacheProvider
```

## Table of Contents

- [Package and namespaces](#package-and-namespaces)
- [Management without HTTP](#management-without-http)
- [`ICacheOrchestrator` surface](#icacheorchestrator-surface)
- [`CacheDomainContext`](#cachedomaincontext)
- [Basic get-or-create](#basic-get-or-create)
- [`CacheEntryRequest`](#cacheentryrequest)
- [One entity](#one-entity)
- [Entity collections](#entity-collections)
- [Factory-owned footprint](#factory-owned-footprint)
- [Disabled domains and failures](#disabled-domains-and-failures)
- [Coordinating Core and HTTP](#coordinating-core-and-http)
- [Provider boundary](#provider-boundary)

## Package and namespaces

```bash
dotnet add package CacheOrchestrator.Core
```

That is the only dependency a reusable library needs. A standalone worker also installs one provider package, for example:

```bash
dotnet add package CacheOrchestrator.FusionCache
```

```csharp
using CacheOrchestrator.DependencyInjection;
using CacheOrchestrator.Entity;
using CacheOrchestrator.Orchestration;
```

A reusable library can reference only Core. A standalone worker host registers Core and exactly one Data Cache provider without ASP.NET Core:

```csharp
builder.Services.AddCacheOrchestratorCore(builder.Configuration);
builder.Services.AddCacheOrchestratorFusionCache(builder.Configuration);
```

Use `AddCacheOrchestratorHybridCache` instead of FusionCache when HybridCache is the chosen provider. Web hosts usually install the `CacheOrchestrator` meta package. See [package composition](../how-to/composition.md#scenario-6).

## Management without HTTP

The same registration provides `ICacheOrchestratorManagement`. It is the application-level management boundary for health and cluster information, domain inspection, invalidation, runtime Version/settings changes, and the settings catalog. It can be called from a worker command, CLI adapter, gRPC service, message consumer, or another secured transport without installing ASP.NET Core.

```csharp
using CacheOrchestrator.Admin;

ICacheOrchestratorManagement management =
    services.GetRequiredService<ICacheOrchestratorManagement>();

AdminDomainMutationResultDto changed = await management.SetVersionAsync(
    "catalog",
    new AdminVersionRequest { Version = "2" },
    cancellationToken);
```

Core reports portable Data Cache configuration and has no resource endpoints to discover by default. A host can implement `IAdminEndpointCatalog` and `IAdminDomainConfigProvider` to add its resource inventory and host-specific policy. `CacheOrchestrator.AspNetCore` provides those adapters and maps the existing Admin API routes onto the same management contract. See [Admin — Management API](admin.md#management-api).

## `ICacheOrchestrator` surface

| API | Key shape | Footprint |
|-----|-----------|-----------|
| `GetOrCreateAsync` | Caller-supplied logical key | Domain tag, plus optional `CacheEntryRequest` footprint and tags |
| `GetOrCreateWithFootprintAsync` | Caller-supplied logical key | Factory returns the final `FootprintCacheBox<T>` |
| `GetOrCreateEntityAsync` | Caller-supplied logical key | Primary entity; optional members, dependencies, and aliases |
| `GetOrCreateEntitySetAsync` | Caller-supplied logical key | Collection members and optional dependencies or aliases |

All methods use `ValueTask` and accept `Func<CancellationToken, ValueTask<...>>`. The provider may invoke the factory on a miss, refresh, bypass, or when Data Cache is disabled. Factories must therefore be safe to run more than once.

## `CacheDomainContext`

`CacheDomainContext` lets the host supply a normalized domain and optional entity kind to a library without making the library hard-code deployment policy names.

```csharp
var catalog = new CacheDomainContext("catalog", entityKind: "products");
```

| Member | Meaning |
|--------|---------|
| `Domain` | Normalized domain name |
| `EntityKind` | Optional normalized entity kind |
| `EntityKindOr(defaultEntityKind)` | Configured kind or normalized fallback |

It does not carry a resource id. The method call supplies the logical key and resource identity.

## Basic get-or-create

The convenience overload builds a `CacheEntryRequest` from the context and logical key:

```csharp
public sealed class CatalogReader(ICacheOrchestrator cache)
{
    public ValueTask<IReadOnlyList<ProductDto>?> GetFeaturedAsync(
        CacheDomainContext domain,
        CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync(
            domain,
            logicalKey: "products:featured",
            async token => await LoadFeaturedAsync(token),
            cancellationToken);
}
```

The physical key is:

```text
co3:{escapedDomain}:{versionHex}:{logicalKey}
```

The logical key must be deterministic, stable across processes, and free of secrets. Core does not derive keys from routes, query strings, headers, or users; those are ASP.NET concerns handled by `IDomainDataCache`.

## `CacheEntryRequest`

Use the request object for the complete low-level application contract:

```csharp
string sku = "ABC-42";
var request = new CacheEntryRequest
{
    Domain = "catalog",
    Key = $"product-by-sku:{sku}",
    Footprint = new EntityFootprint(
        primary: new EntityRef("products-by-sku", sku)),
    AdditionalTags = ["catalog-import:2030-08"]
};

ProductDto? product = await cache.GetOrCreateAsync(
    request,
    async token => await LoadProductBySkuAsync(sku, token),
    cancellationToken);
```

| Property | Required | Meaning |
|----------|----------|---------|
| `Domain` | Yes | Domain name; normalized before resolution |
| `Key` | Yes | Logical key; CacheOrchestrator prepends the domain and Version |
| `Footprint` | No | Early primary/member/dependency/alias tags |
| `AdditionalTags` | No | Advanced custom invalidation tags; the domain tag is still added automatically |

The physical-key bypass is internal to the trusted ASP.NET Core host path, so application code cannot accidentally omit domain and Version isolation.

## One entity

Use `GetOrCreateEntityAsync` when one logical entry represents one primary entity:

```csharp
public ValueTask<ProductDto?> GetProductAsync(
    CacheDomainContext domain,
    int productId,
    CancellationToken cancellationToken) =>
    cache.GetOrCreateEntityAsync(
        domain,
        logicalKey: $"product:{productId}",
        resourceId: productId,
        async token => await LoadProductAsync(productId, token),
        defaultEntityKind: "products",
        cancellationToken);
```

The primary tag is `entity:{domain}:{entityKind}:{resourceId}`. Returning `null` stores a negative result for that primary identity when the provider supports the operation.

Use the `EntityCache<T>` factory overload when the value also depends on other entities:

```csharp
ProductDto? product = await cache.GetOrCreateEntityAsync(
    domain,
    logicalKey: $"product:{productId}:details",
    resourceId: productId,
    async token =>
    {
        ProductDto? value = await LoadProductAsync(productId, token);
        return value is null
            ? EntityCache.Miss<ProductDto>()
            : EntityCache.Create(value)
                .DependsOn("brands", value.BrandId)
                .Alias("products-by-sku", value.Sku);
    },
    cancellationToken: cancellationToken);
```

The `CacheDomainContext` entity overloads, `Members`, `DependsOn`, and `Alias` accept generic IDs. Prefer natural values such as `42` or a `Guid`; CacheOrchestrator formats `IFormattable` IDs with invariant culture. Low-level `EntityRef` uses a string because it is the serialized footprint contract.

## Entity collections

`GetOrCreateEntitySetAsync` stores the collection under the logical key and tags the entry with every returned member:

```csharp
IReadOnlyList<ProductDto> products = await cache.GetOrCreateEntitySetAsync(
    domain: "catalog",
    logicalKey: $"category:{categoryId}:products",
    entityKind: "products",
    async token =>
    {
        IReadOnlyList<ProductDto> rows = await LoadCategoryAsync(categoryId, token);
        return EntitySet.Create(rows, product => product.Id)
            .DependsOn("categories", categoryId);
    },
    cancellationToken);
```

Invalidating product `42` retires every set whose returned members included `42`. Invalidating the category retires the filtered set through its dependency tag.

## Factory-owned footprint

`GetOrCreateWithFootprintAsync` is the lowest-level footprint-aware operation. The request supplies early tags; the factory returns the stored value and final footprint in `FootprintCacheBox<T>`.

Use it when neither the entity nor collection convenience overload describes the cached graph. On a factory run, CacheOrchestrator writes the value again only when the final footprint expands the early tags. Prefer `GetOrCreateEntityAsync` or `GetOrCreateEntitySetAsync` for normal application code.

## Disabled domains and failures

- If `DataCache.Enabled` is `false`, the factory runs uncached.
- An unknown or invalid domain is resolved through the same `IDomainCacheOptionsProvider` rules as HTTP callers.
- Factory cancellation and exceptions propagate to the caller.
- Provider behavior for fail-safe, timeouts, serialization, and distributed storage depends on FusionCache, HybridCache, or a custom `IDataCacheProvider`.

## Coordinating Core and HTTP

A library and its web host should use the same domain and entity kind:

```csharp
var catalog = new CacheDomainContext("catalog", entityKind: "products");

app.MapGet("/api/products/{id:int}", async (
    int id,
    CatalogReader reader,
    CancellationToken cancellationToken) =>
{
    ProductDto? product = await reader.GetProductAsync(catalog, id, cancellationToken);
    return product is null ? Results.NotFound() : Results.Ok(product);
})
.CacheOutputWithDomain(
    catalog.Domain,
    resourceRouteKey: "id",
    entityKind: catalog.EntityKind!);
```

Core and HTTP keys are not the same shape, but their domain and entity tags align, so one `InvalidateEntityAsync("catalog", "products", 42)` call retires both layers.

## Provider boundary

Applications should depend on `ICacheOrchestrator`, not `IDataCacheProvider`. Provider authors implement `IDataCacheProvider` and receive a fully formed `DataCacheProviderRequest` containing the physical key, instance name, tags, and the HTTP-free `DomainCacheOptions`. `GetOrCreateAsync<T>` returns `DataCacheProviderResult<T>` so the provider can distinguish a value materialized by this call from a cached or stale value. Provider-specific runtime settings are resolved by the provider package rather than added to the Core request contract. See [Extensibility](extensibility.md#data-cache-engine-idatacacheprovider).

## Related

- [Data Cache](data-cache.md) — HTTP request-scoped API
- [Entity footprint](entity-footprint.md) — footprint patterns and invalidation behavior
- [Invalidation](invalidation.md) — domain, entity, kind, and custom-tag invalidation
- [Cache keys](cache-keys.md) — Core and HTTP key composition
- [Extensibility](extensibility.md) — provider and host extension points
- [Package composition](../how-to/composition.md#scenario-6) — class library and host wiring
