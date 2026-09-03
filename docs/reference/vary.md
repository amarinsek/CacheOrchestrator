# Domain vary dimensions

> **Reference** — shared vary dimensions for Output Cache and Data Cache (query, headers, auth, contributors).

CacheOrchestrator shares one **vary model** between **Output Cache** and **FusionCache** (where it makes sense). Built-in toggles and allowlists live on the domain; apps can add small custom dimensions via `ICacheVaryContributor` without replacing `IDomainKeyGenerator`.

**Endpoint cache identity** (`.WithCacheIdentity` / `[CacheIdentity]`, content-hash helpers) is **not** domain vary configuration. It is a per-endpoint, per-method binding that can add `co-id:*` material to Output Cache `VaryByValues` and Data Cache keys. See [endpoint cache identity](cache-identity.md) and [cache keys](cache-keys.md).

See also: [cache-keys.md](cache-keys.md), [Output Cache](output-cache.md), [configuration.md](configuration.md).

Admin Console App **Operations → Patch settings** can change these at runtime (bool / enum / numbers and comma-separated string lists). Playground domain **`vary-demo`** (`GET /api/vary-demo`) exercises Accept + `?lang=` allowlisting.

## Table of Contents

- [Built-in settings](#built-in-settings)
- [Authorization](#authorization)
- [Custom vary (`ICacheVaryContributor`)](#custom-vary-icachevarycontributor)
- [Security checklist](#security-checklist)

## Built-in settings

All under `Cache:Domains:{name}:` (and `DomainDefaults`).

| Setting | Default | Output Cache | Data Cache | Notes |
|---------|---------|--------------|------------|-------|
| `VaryByAccept` | `true` | ✓ | ✓ | Separates negotiated representations such as JSON and XML. Disable it only when the endpoint always produces the same representation. |
| `AcceptNormalizationList` | `null` | normalize | normalize | Optional prefer-list used to collapse equivalent `Accept` values. Without a list, the raw header value is used. |
| `VaryByAcceptLanguage` | `false` | ✓ | ✓ | Locale. Stays off — most JSON APIs are language-agnostic; enabling fragments the cache. |
| `AcceptLanguageNormalizationList` | `null` | normalize | normalize | e.g. `en`, `sl` when you opt into language vary |
| `VaryByHeaders` | `null` | ✓ | ✓ | Case-insensitive names; sensitive values hashed. Never auto-fill. |
| `VaryByQueryKeys` | `null` | ✓ | ✓ | `null` = all non-tracking; `[]` = none; non-empty = allowlist |
| `IgnoreQueryKeys` | `null` | ✓ | ✓ | Extra deny list on top of tracking prefixes |
| `VaryByCookies` | `null` | ✓ | ✓ | Cookie **names** only; values always hashed. Opt-in only (CSRF / session risks). |
| `EmitResponseVary` | **`true`** | ✓ | — | Emits HTTP response `Vary` for non-secret headers included in the key. Set `false` to omit the response header without changing server-side key material. |

The layer-specific dimensions live in their nested sections: `DataCache.VaryOnEncoding`, `DataCache.VaryOnPublicAddress`, `OutputCache.EncodingNormalizationList`, and `OutputCache.VaryByHost`.

Normalization changes cache identity, not the request. CacheOrchestrator stores the selected prefer-list value as named vary material for both Output Cache and Data Cache while the endpoint handler continues to see the original `Accept`, `Accept-Language`, and `Accept-Encoding` headers. The response `Vary` header still advertises the corresponding non-secret request header.

> **Note on query parameters:** Unlike native ASP.NET Core Output Caching (which ignores query parameters by default), CacheOrchestrator's default (`"VaryByQueryKeys": null`) **varies by all query parameters** (except tracking parameters like `utm_*`). To ignore all query parameters like native ASP.NET Core does, explicitly set `"VaryByQueryKeys": []`.

### Query key examples

**Allowlist** — only listed keys enter the cache key; everything else is ignored:

```json
{
  "catalog": {
    "VaryByQueryKeys": [ "page", "pageSize", "sort" ]
  }
}
```

**Ignore list** — with default `VaryByQueryKeys` (`null`), every non-tracking query key enters the cache key except those listed:

```json
{
  "search": {
    "IgnoreQueryKeys": [ "debug" ]
  }
}
```

**No query vary** — empty allowlist: query string never partitions the key (closest to native ASP.NET Core Output Cache default):

```json
{
  "static-asset": {
    "VaryByQueryKeys": []
  }
}
```

## Authorization

| Setting | Default | Notes |
|---------|---------|-------|
| `AuthBypassMode` | `AuthenticatedOrAuthorization` | Controls which authentication signals bypass server caching. |
| `VaryOutputCacheByUser` | `true` | Partition by user / Authorization hash when caching auth |
| `VaryByAuthClaims` | `null` | Claim types in auth-user material (e.g. `tenant_id`). App-specific. |
| `AuthVaryIncludeAuthorizationHash` | `true` | Fallback hash of `Authorization` when no identity |
| `TreatAuthorizationAsAuthSignal` | `true` | `Authorization` counts for OR-mode / vary (API keys / gateways) |
| `DataCacheRespectAuthBypass` | **`true`** | Data Cache skips when Output Cache would bypass for authentication. Set `false` only for [shared Data Cache under Output Cache auth bypass](#shared-data-cache-under-output-cache-auth-bypass). |
| `ClientCache.ForcePrivateWhenAuthenticated` | `true` | Changes a configured public Client Cache policy to private for a signed-in identity. |

There is no inverse “Output Cache respects Data Cache” setting: Output Cache owns the request bypass through `AuthBypassMode`. `DataCacheRespectAuthBypass` only decides whether Data Cache follows the same signal.

Fusion includes **auth-user** in the key only when `AuthBypassMode` is `Never` **or** `VaryByAuthClaims` is set (`ShouldIncludeAuthUserVary`). Output Cache still varies by `auth-user` whenever authenticated traffic is cached and `VaryOutputCacheByUser` is true.

### `AuthBypassMode`

| Value | Bypass when |
|-------|-------------|
| `Never` | Never auto-bypass |
| `AuthenticatedIdentityOnly` | Only `User.Identity.IsAuthenticated` |
| `AuthorizationHeaderOnly` | Only `Authorization` header |
| `AuthenticatedOrAuthorization` | Default (Identity **or** Authorization) |

**Private dashboard (cache Output Cache and Data Cache per user):**

```json
{
"user-dashboard": {
  "AuthBypassMode": "Never",
  "VaryOutputCacheByUser": true,
  "VaryByAuthClaims": [ "tenant_id" ],
  "ClientCache": { "Cacheability": "Private" }
}
}
```

### Shared Data Cache under Output Cache auth bypass

Default `DataCacheRespectAuthBypass: true` means: if Output Cache would auth-bypass, the Data Cache also runs the factory uncached. That is the safe parity default.

Set **`false`** when the HTTP response must not be stored in Output Cache for authenticated traffic, but the **Data Cache payload is shared** (the same for every caller):

```text
Signed-in user (cookie / Identity)
  → AuthBypassMode = AuthenticatedOrAuthorization (default)
  → Output Cache: BYPASS (do not store the full HTTP response)
  → GetOrSet("products"): shared catalogue for everyone

DataCacheRespectAuthBypass = true  → Data Cache also BYPASS → factory on every request
DataCacheRespectAuthBypass = false → Output Cache still bypasses; Data Cache keeps caching shared data
```

```json
{
"products": {
  "DataCacheRespectAuthBypass": false
}
}
```

Use this when a dashboard shell is authenticated but `IDomainDataCache.GetOrSet*` loads public/shared domain data. Prefer **not** using `false` for “public tiles with an API key” — that pattern is better as `AuthBypassMode: Never` (and usually `VaryOutputCacheByUser: false` / `TreatAuthorizationAsAuthSignal: false`) so **both** layers intentionally cache.

### Restrict Accept normalization

```json
{
"DomainDefaults": {
  "VaryByAccept": true,
  "AcceptNormalizationList": [ "application/json", "application/xml" ]
}
}
```

## Custom vary (`ICacheVaryContributor`)

```csharp
public sealed class TenantVaryContributor : ICacheVaryContributor
{
    public int Order => 100;

    public void Contribute(CacheVaryContext context, ICacheVaryBuilder builder)
    {
        string? tenant = context.HttpContext.User.FindFirst("tenant_id")?.Value;
        if (!string.IsNullOrEmpty(tenant))
            builder.AddValue("tenant", tenant);
    }
}

// Registration
services.AddSingleton<ICacheVaryContributor, TenantVaryContributor>();
```

`CacheVaryContext.Surface` tells the contributor which key is being built:

| Value | Consumer |
|-------|----------|
| `CacheVarySurface.OutputCache` | ASP.NET Core Output Cache vary values |
| `CacheVarySurface.Fusion` | HTTP Data Cache key material |

Most contributors should add the same dimension to both surfaces, as the example does. Branch on `Surface` only when the payload or sharing boundary genuinely differs; otherwise the two layers can disagree about request identity.

- Do **not** pass raw secrets to `AddValue` — use `AddHashedValue`.
- Sensitive header names (`Authorization`, `Cookie`, `X-Api-Key`, …) added via `AddHeader` are hashed automatically and never advertised on response `Vary`.
- Full key-shape replacement is still available via `IDomainKeyGenerator`.
- The full application extension-point catalog is in [Extensibility](extensibility.md).

## Security checklist

> [!IMPORTANT]
> Vary settings change what enters keys and what browsers see. Before enabling extra dimensions in production:
>
> - [ ] Never put raw secrets in custom vary via `AddValue` — use `AddHashedValue` (or hashed header helpers)
> - [ ] Treat `VaryByCookies` as opt-in only; document CSRF / session-fixation risk if you partition by session cookies
> - [ ] Keep Client Cache `private` (or stricter) when Output Cache varies by authenticated user
> - [ ] Confirm response `Vary` does not advertise secret-bearing headers (library omits those; do not work around it)
> - [ ] Prefer tight `VaryByHeaders` / query allowlists — wide vary multiplies entries and can leak tenancy signals into key cardinality
>
> Library defaults already keep raw `Authorization` / cookie values out of keys, vary dictionaries, logs, and `X-CacheOrchestrator`. Startup validation rejects empty allowlist entries and caps sizes (e.g. max 8 headers / cookies).

## Related

- [Guide — concepts](../guide/concepts.md) — domain model and layer overview  
- [Output Cache](output-cache.md) — auth bypass on the policy  
- [cache-keys.md](cache-keys.md) — how vary material enters Data Cache keys  
- [configuration.md](configuration.md) — domain vary flags  
- [faq.md](../guide/faq.md) — authenticated requests and JSON vs XML  
