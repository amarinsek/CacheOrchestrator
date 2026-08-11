# CacheOrchestrator Sample Playground

[CacheOrchestrator](../..) is domain-based caching for ASP.NET Core that orchestrates Output Cache, FusionCache, and client Cache-Control under the same model.

Interactive playground for **TTL, Client Cache Schedule, Redis, multi-instance, and CRUD** — after you already know the happy path.

> New to CacheOrchestrator? Start with the **[Minimal sample](../CacheOrchestrator.Minimal)** first.  

This project shows the core value proposition: **configuring multi-tier caching without polluting endpoint code**.

![Sample screenshot](../../docs/assets/sample-playground.png)

## Getting Started

1. Ensure you have the .NET SDK installed.
2. Run the project from the repository root:

   ```bash
   dotnet run --project samples/CacheOrchestrator.Sample
   ```
3. Open your browser to the printed URL (default **http://localhost:5289**).

> *Note: Keep your terminal visible for logs, and open DevTools → Network with **Disable cache** so you can observe server-side Open cache and Fusion Cache hits (not only the browser cache).*

## Core Concepts in this Sample

### 1. Zero-code Endpoints
The endpoints you see in the dropdown (like `/api/catalog` or `/api/products/{id}`) are not hardcoded with caching logic in `Program.cs`. Instead, they are completely driven by `appsettings.json`.

If you want to add a new test endpoint, simply add a line to the `Demo:Endpoints` array in `appsettings.json` and restart the application.

### 2. Configuration via JSON Editor
Instead of a rigid UI form, this sample includes an embedded JSON editor (powered by CodeMirror) that lets you directly edit the `appsettings.json` file in your browser.

- Click **"appsettings.json"** in the top right.
- Change a value (for example, lower the `OutputCacheTtlSeconds` for the `catalog` domain).
- Click **Save & reload config**.

**How it works:**
ASP.NET Core's `IOptionsMonitor` automatically detects changes to the JSON file on disk and reloads the configuration seamlessly without requiring a server restart. Our backend API catches the save event and proactively invalidates the cached domains so you see your changes immediately.

> [!WARNING]
> **Development Use Only**
> Editing `appsettings.json` via a web request is exclusively designed for this development playground. In a production environment, configuration files should never be writable by the application process.

### 3. The `Client Cache Schedule` Phase
When you define a `ScheduledUpdateUtc` for a domain in the JSON configuration, `CacheOrchestrator` automatically begins managing the `max-age` of the `Cache-Control` header.

Watch the UI tags when you fetch an endpoint:
- **<span style="color:#3dd68c">calm</span>**: Far from the update; TTL is at maximum.
- **<span style="color:#f5c842">approaching</span>**: Nearing the update; TTL is ramping down linearly.
- **<span style="color:#f5924e">hold</span>**: The scheduled time has passed; TTL is floored at its minimum to allow for a safe deployment window.

## Pro Tips for the Playground

> **Testing Server-Side Caching**
> Because `CacheOrchestrator` emits standard HTTP `Cache-Control` headers, your browser will aggressively cache responses. To properly test and observe the **Server-Side Cache** (`OC-HIT` and `FC-HIT`), you must either:
> 1. Check **"Disable cache"** in your browser's Dev Tools Network tab.
> 2. Or set `"ClientTtlSeconds": 1` for your domain in the JSON editor so the browser expires it immediately.

### Understanding the Cache Tags (Multi-Tier Caching)
When you fetch data, you will see a badge indicating where the response came from. `CacheOrchestrator` implements a powerful multi-tier caching architecture, and the UI makes this visible:

- **`BROWSER-CACHE`**: The request never even hit the network! The browser served it directly from its local disk or memory cache.
- **`OC-HIT`**: The request reached the server, but was intercepted immediately by the ASP.NET Core Output Cache. The application code and database were bypassed entirely.
- **`OC-MISS` `FC-HIT`**: The Output Cache missed (or expired), so the request reached the application layer. However, `FusionCache` (L1/L2) still had the data in memory/Redis! The application returned the cached data without hitting the database.
- **`MISS`**: Both Output Cache and FusionCache missed. The application had to fetch the data from the underlying data store.

### Playground Checkboxes

#### Append `utm_source=demo`
This checkbox appends a tracking parameter to your URL (e.g. `/api/catalog?utm_source=demo`). 
**What happens?** You will still get a cache **HIT**! 
`CacheOrchestrator` automatically strips known tracking parameters (like `utm_*`, `gclid`, `fbclid`) from the cache key. This means marketing campaigns won't bust your cache and take down your server, while still allowing the client browser to track the source.

#### Send `Cache-Control: no-store`
Checking this will send the `Cache-Control: no-store` header with your request. 
**What happens?** You will get a cache **MISS**. 
`CacheOrchestrator` respects HTTP semantics. When a client explicitly requests `no-store` (often done via hard-refresh in the browser), the server will bypass the Output Cache to serve the freshest possible data.

### Understanding HTTP 304 (Not Modified)
The JavaScript `fetch` API is designed to hide HTTP caching complexity. When a response is served directly from the browser's cache (or via a fast `304 Not Modified` validation with the server), `fetch` reports a `status: 200` to the UI. 

To see exactly what the browser is doing over the network (e.g., verifying if the server responded with a `304`), **open your browser's Dev Tools Network tab**.

### Dynamic CRUD (entity invalidation)

The sample also exposes a small **product CRUD** playground that keeps `Version` stable and purges a single entity after an update:

| Method | URL | Behaviour |
|--------|-----|-----------|
| `GET` | `/api/crud/products/{id}` | OC + FC with domain `product-crud`, resource id from route `id` |
| `PUT` | `/api/crud/products/{id}` | Body `{ "name": "...", "price": 12.5 }` then `InvalidateEntityAsync` |
| `GET` | `/api/crud/products` | List in-memory “DB” (uncached) |

Try:

```bash
# 1) Load product (MISS, then HIT)
curl -i http://localhost:5289/api/crud/products/42

# 2) Update price under the same Version
curl -i -X PUT http://localhost:5289/api/crud/products/42 \
  -H "Content-Type: application/json" \
  -d "{\"name\":\"Demo Widget\",\"price\":12.5}"

# 3) Load again — should miss cache and show price 12.5
curl -i http://localhost:5289/api/crud/products/42
```

Background: [docs/domain-profiles.md](../../docs/domain-profiles.md).

### Redis Support
By default, the sample runs completely in-memory. The sample project references **CacheOrchestrator.Redis** and calls `AddRedisBackend()` in `Program.cs`, so you can switch providers in config without code changes.

If you want to see how `CacheOrchestrator` behaves in a distributed setup:

1. Start a local Redis container. The quickest way is using Docker:
   ```bash
   docker run -d --name redis-demo -p 6379:6379 redis:7-alpine
   ```
2. In the Sample UI, open the **appsettings.json** editor.
3. A very common and recommended production setup is to keep `OutputCache` in-memory (for maximum HTTP throughput) but distribute `FusionCache` across instances. Update the `Cache` section to look like this:
   ```json
   "Cache": {
     "Namespace": "sample",
     "OutputCache": {
       "Provider": "InMemory"
     },
     "FusionCacheInstances": {
       "default": {
         "Provider": "Redis"
       }
     },
     "Redis": {
       "Configuration": "localhost:6379"
     },
    }
   ```
4. Click **Save & reload config**.

#### Advanced Redis Setup (Multi-Instance)
`CacheOrchestrator` allows you to configure multiple, completely isolated FusionCache instances, each talking to a different Redis cluster, and then map specific caching domains to those instances via the domain property **`FusionCacheInstance`** (not a top-level Redis key).

1. Start a second Redis container on a different port:
   ```bash
   docker run -d --name redis-catalog -p 6380:6379 redis:7-alpine
   ```
2. Update your `appsettings.json` to define two `FusionCacheInstances` and point domains at them:

```json
"Cache": {
  "FusionCacheInstances": {
    "default": {
      "Provider": "Redis",
      "Redis": { "Configuration": "localhost:6379" }
    },
    "catalog-cluster": {
      "Provider": "Redis",
      "Redis": { "Configuration": "localhost:6380" }
    }
  },
  "Domains": {
    "product-detail": {
      "FusionCacheInstance": "default"
    },
    "catalog": {
      "FusionCacheInstance": "catalog-cluster"
    }
  }
}
```

In this scenario, `product-detail` objects use the `default` Fusion instance (`localhost:6379`), while `catalog` uses `catalog-cluster` (`localhost:6380`).

Key config names:

| Config path | Meaning |
|-------------|---------|
| `Cache:FusionCacheInstances:{name}` | Named FusionCache instance (provider + Redis) |
| `Cache:Domains:{domain}:FusionCacheInstance` | Which instance that domain uses |
| `Cache:Redis` | Optional shared Redis connection defaults (package `CacheOrchestrator.Redis`) |

Namespace reminder: the `default` Fusion instance keys use `{Namespace}-fc` (no `-default` suffix).