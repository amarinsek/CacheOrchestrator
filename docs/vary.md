# Domain vary dimensions

> **Reference.** Product overview: [root README](../README.md). Orientation: [Guide — concepts](guide/concepts.md). Catalog: [documentation index](README.md). Canonical detail for Accept / auth / contributors.

CacheOrchestrator shares one **vary model** between **Output Cache** and **FusionCache** (where it makes sense). Built-in toggles and allowlists live on the domain; apps can add small custom dimensions via `ICacheVaryContributor` without replacing `IDomainKeyGenerator`.

See also: [cache-keys.md](cache-keys.md), [output-cache.md](output-cache.md), [configuration.md](configuration.md).

Admin Console **Operations → Patch settings** can change these at runtime (bool / enum / numbers and comma-separated string lists). Playground domain **`vary-demo`** (`GET /api/vary-demo`) exercises Accept + `?lang=` allowlisting.

## Built-in settings

All under `Cache:Domains:{name}:` (and `DomainDefaults`).

| Setting | Default | OC | Fusion | Notes |
|---------|---------|----|--------|-------|
| `VaryByAccept` | `false` | ✓ | ✓ | Content negotiation (`Accept`). Planned **`true`** in 3.0.0 (with a JSON/XML prefer-list) so formats cannot share one entry by accident; opt-in until then to avoid fragmenting JSON-only APIs. |
| `AcceptNormalizationList` | `null` | normalize | same | Prefer-list (like encoding). Planned default in 3.0.0: `["application/json", "application/xml"]` when paired with `VaryByAccept`. |
| `VaryByAcceptLanguage` | `false` | ✓ | ✓ | Locale. Stays off — most JSON APIs are language-agnostic; enabling fragments the cache. |
| `AcceptLanguageNormalizationList` | `null` | normalize | same | e.g. `en`, `sl` when you opt into language vary |
| `VaryByHeaders` | `null` | ✓ | ✓ | Case-insensitive names; sensitive values hashed. Never auto-fill. |
| `VaryByQueryKeys` | `null` | ✓ | ✓ | `null` = all non-tracking; `[]` = none; non-empty = allowlist |
| `IgnoreQueryKeys` | `null` | ✓ | ✓ | Extra deny list on top of tracking prefixes |
| `VaryByCookies` | `null` | ✓ | ✓ | Cookie **names** only; values always hashed. Opt-in only (CSRF / session risks). |
| `EmitResponseVary` | **`true`** | ✓ | — | HTTP response `Vary` for non-secret headers we varied on. Set `false` to omit (2.1-like silence). |

Existing flags stay: `FusionCacheVaryOnEncoding`, `EncodingNormalizationList`, `FusionCacheVaryOnPublicAddress`, `OutputCacheVaryByHost`.

> **Note on query parameters:** Unlike native ASP.NET Core Output Caching (which ignores query parameters by default), CacheOrchestrator's default (`"VaryByQueryKeys": null`) **varies by all query parameters** (except tracking parameters like `utm_*`). To ignore all query parameters like native ASP.NET Core does, explicitly set `"VaryByQueryKeys": []`.

### Query allowlist examples

```json
"catalog": {
  "VaryByQueryKeys": [ "page", "pageSize", "sort" ],
  "IgnoreQueryKeys": [ "debug" ]
}
```

```json
"static-asset": {
  "VaryByQueryKeys": []
}
```

## Authorization

| Setting | Default | Notes |
|---------|---------|-------|
| `AuthBypassMode` | `AuthenticatedOrAuthorization` (or derived from legacy bool) | Prefer this over legacy `BypassWhenAuthenticated`. Planned 3.0.0: drop the obsolete bool; mode remains the control. |
| `BypassWhenAuthenticated` | `true` | **Obsolete** — `true` → `AuthenticatedOrAuthorization`, `false` → `Never` (ignored when `AuthBypassMode` is set). Removed in 3.0.0. |
| `VaryOutputCacheByUser` | `true` | Partition by user / Authorization hash when caching auth |
| `VaryByAuthClaims` | `null` | Claim types in auth-user material (e.g. `tenant_id`). App-specific. |
| `AuthVaryIncludeAuthorizationHash` | `true` | Fallback hash of `Authorization` when no identity |
| `TreatAuthorizationAsAuthSignal` | `true` | `Authorization` counts for OR-mode / vary (API keys / gateways) |
| `DataCacheRespectAuthBypass` | **`true`** | Data cache skips when OC would auth-bypass (parity). Set `false` only for [shared data cache under OC auth bypass](#shared-data-cache-under-oc-auth-bypass). |
| `ClientForcePrivateWhenAuthenticated` | `true` | Public → private clamp for signed-in Identity |

There is no separate “OC respect data cache” flag: Output Cache already owns bypass via `AuthBypassMode`. `DataCacheRespectAuthBypass` only answers whether the data cache follows that same signal.

Fusion includes **auth-user** in the key only when `AuthBypassMode` is `Never` **or** `VaryByAuthClaims` is set (`ShouldIncludeAuthUserVary`). Output Cache still varies by `auth-user` whenever authenticated traffic is cached and `VaryOutputCacheByUser` is true.

### `AuthBypassMode`

| Value | Bypass when |
|-------|-------------|
| `Never` | Never auto-bypass |
| `AuthenticatedIdentityOnly` | Only `User.Identity.IsAuthenticated` |
| `AuthorizationHeaderOnly` | Only `Authorization` header |
| `AuthenticatedOrAuthorization` | Default (Identity **or** Authorization) |

**Private dashboard (cache OC + Fusion per user):**

```json
"user-dashboard": {
  "AuthBypassMode": "Never",
  "VaryOutputCacheByUser": true,
  "VaryByAuthClaims": [ "tenant_id" ],
  "ClientCache": { "Cacheability": "Private" }
}
```

### Shared data cache under OC auth bypass {#shared-data-cache-under-oc-auth-bypass}

Default `DataCacheRespectAuthBypass: true` means: if Output Cache would auth-bypass, the data cache also runs the factory uncached. That is the safe parity default.

Set **`false`** when the HTTP response must not be OC-cached for authenticated traffic, but the **data-cache payload is shared** (same for every caller):

```text
Signed-in user (cookie / Identity)
  → AuthBypassMode = AuthenticatedOrAuthorization (default)
  → Output Cache: BYPASS  (do not store the full HTTP response)
  → GetOrSet("products"): shared catalogue for everyone

DataCacheRespectAuthBypass = true  → data cache also BYPASS → factory on every request
DataCacheRespectAuthBypass = false → OC still bypasses; data cache keeps caching shared data
```

```json
"products": {
  "DataCacheRespectAuthBypass": false
}
```

Use this when a dashboard shell is authenticated but `IDomainFusionCache.GetOrSet*` loads public/shared domain data. Prefer **not** using `false` for “public tiles with an API key” — that pattern is better as `AuthBypassMode: Never` (and usually `VaryOutputCacheByUser: false` / `TreatAuthorizationAsAuthSignal: false`) so **both** layers intentionally cache.

### Opt into Accept vary early (planned 3.0 default)

```json
"DomainDefaults": {
  "VaryByAccept": true,
  "AcceptNormalizationList": [ "application/json", "application/xml" ]
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

- Do **not** pass raw secrets to `AddValue` — use `AddHashedValue`.
- Sensitive header names (`Authorization`, `Cookie`, `X-Api-Key`, …) added via `AddHeader` are hashed automatically and never advertised on response `Vary`.
- Full key-shape replacement is still available via `IDomainKeyGenerator`.

## Security

- Raw `Authorization` / cookie values never enter keys, vary dictionaries, logs, or `X-Cache`.
- Cookie vary is opt-in only; document CSRF/session fixation risks.
- Response `Vary` omits secrets-bearing headers; per-user OC still needs `private` client/CDN policy.
- Startup validation rejects empty allowlist entries and caps sizes (e.g. max 8 headers / cookies).

## Related

- [Guide — concepts](guide/concepts.md)
- [output-cache.md](output-cache.md) — auth bypass on the policy
- [cache-keys.md](cache-keys.md) — how vary material enters Fusion keys
- [configuration.md](configuration.md) — domain flags
- [faq.md](faq.md) — authenticated requests and JSON vs XML
