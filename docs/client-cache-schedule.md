# Client Cache Schedule

> **Guide.** Product overview: [root README](../README.md). Orientation: [Guide — concepts](guide/concepts.md). Catalog: [documentation index](README.md). Canonical algorithm and settings are on this page.

In systems where large datasets are updated in scheduled batches (e.g., mapping applications where the entire set of aerial/satellite imagery is replaced once a year, or monthly catalog extracts), you face a classic caching dilemma.

To minimize server load and maximize performance, you want clients (browsers and CDNs) to have a very long `max-age`. However, if clients cache the data for a month right before your scheduled update, they will continue seeing the outdated satellite imagery long after the origin has been refreshed. 

**Client Cache Schedule** solves this by adjusting the allowed cache lifetime based on the time remaining until the next planned update (`ScheduledUpdateUtc`). For most of the dataset's life, it keeps a **very long `max-age`**, but as the scheduled update approaches, the lifetime **gradually ramps down toward a short floor** (e.g., 15 minutes). This ensures that clients are perfectly primed to fetch the new generation of data exactly when it lands, without forcing you to use a tiny `max-age` for the entire year.

This changes only the **client** `Cache-Control` header. Output Cache and the data cache keep their own independent TTLs (`OutputCache.TtlSeconds`, `DataCache.TtlSeconds`, plus Fusion-only `fusionCache.hardTtlSeconds` / `FailSafeSeconds` when using the Fusion provider).

The playground sample shows the phases live. Implementation: `ClientCacheHeaderGenerator`, `ClientCacheSchedulePhase`.

---

## How it works

CacheOrchestrator calculates the optimal `max-age` dynamically for every client request on the fly. By observing the distance to the `ScheduledUpdateUtc`, it guarantees that no client receives a `max-age` that would outlive the planned cutover.

<img src="../docs/assets/scheduled-update.svg" height="350" />

<br>

1. **Calm Phase (Long TTL):** Far away from the cutover, clients receive the full `ClientCache.TtlSeconds`. This maximizes cache hits and minimizes server load.
2. **Approaching Phase (Ramp-down):** As the cutover time draws near, the `max-age` is dynamically shortened for each incoming request until it hits the floor (`ClientCache.TtlMinSeconds`).
3. **Hold Phase (Operator Verification):** After the scheduled time passes, the system enters the Hold phase. Clients continue to receive the short `ClientCache.TtlMinSeconds`. This gives operators a safe window to perform the deployment, verify the new data in production, and catch any issues—all while clients are recovering quickly due to the short TTL.
4. **Reset:** Once the operator confirms the update is successful, they set a new `ScheduledUpdateUtc` for the next batch (or remove it entirely). The system immediately returns to the Calm phase, restoring the long `max-age` for all clients.

---

## Observability

Every eligible response that goes through the Output Cache header path reports the phase:

- **X-Cache** — `phase=calm`, `phase=approaching`, `phase=hold`, or `phase=n/a`.
- **Metrics** — counter `cache_orchestrator.client.schedule` with tags `domain` and `phase` (same strings).

Meter name: `CacheOrchestrator`. See [observability.md](observability.md).

## Settings

Under `Cache:DomainDefaults` / `Cache:Domains:{name}`, nested **`ClientCache`** (and independent server TTLs):

- **`ClientCache.Cacheability`** — `Public`, `Private`, or `NoStore`. `NoStore` turns the schedule off.
- **`ClientCache.TtlSeconds`** — target `max-age` when far from cutover, and when there is no schedule.
- **`ClientCache.TtlMinSeconds`** — floor `max-age` near cutover, after cutover, and during the hold after a Version change.
- **`ClientCache.ScheduledUpdateUtc`** — planned cutover (UTC). Omit it for a constant client TTL.
- **`ClientCache.MustRevalidateNearUpdate`** — at the floor, append `must-revalidate`.
- **`Version`** — generation stamp (also used for server keys and ETag).
- **`OutputCache.TtlSeconds`** / **`DataCache.TtlSeconds`** — server layers; not driven by the schedule.

### Example (map tiles / periodic dataset)

```json
"maps-satellite": {
  "Version": "v1",
  "DataCache": {
    "TtlSeconds": 3600
  },
  "OutputCache": {
    "TtlSeconds": 300
  },
  "ClientCache": {
    "Cacheability": "Public",
    "TtlSeconds": 2592000,
    "TtlMinSeconds": 900,
    "ScheduledUpdateUtc": "2026-12-01T00:00:00Z",
    "MustRevalidateNearUpdate": true
  }
}
```

Interpretation:

- Most of the year: clients may keep tiles for **30 days**.  
- Inside the last 30 days before cutover: `max-age` **ramps down** toward **15 minutes**.  
- After cutover (until you set a new schedule/version): stay at **15 minutes**.  
- Near the floor: `must-revalidate` so intermediaries recheck when stale.

---

## Phases (`ClientCacheSchedulePhase`)

| Phase | X-Cache / metrics | When | Client `max-age` |
|-------|-------------------|------|------------------|
| **Calm** | `calm` | `secondsUntilSchedule >= ClientTtlSeconds` | `ClientCache.TtlSeconds` (max; snapshot `ClientTtlSeconds`) |
| **Approaching** | `approaching` | Inside the ramp window before cutover | Linear between max and min |
| **Hold** | `hold` | `now >= ScheduledUpdateUtc` | `ClientCache.TtlMinSeconds` (min; snapshot `ClientTtlMinSeconds`) |
| **NotApplicable** | `n/a` | `NoStore`, blocked, no schedule path, or no client TTL built | `no-store` / N/A or constant max when schedule is null |

Returned from `ClientCacheHeaderGenerator.Build`, written to **`X-Cache`**, and recorded on **`cache_orchestrator.client.schedule`**.

---

## Algorithm (exact)

Inputs (after clamps):

- `max = max(1, ClientTtlSeconds)`  
- `min = clamp(ClientTtlMinSeconds, 1, max)`  

### 1. NoStore

→ `Cache-Control: no-store`, phase `NotApplicable`.

### 2. No schedule

If `ScheduledUpdateUtc` is null:

→ `max-age = max`, phase `NotApplicable`.

### 3. Hold

12:00 UTC (The Cutover / Update time passes)

→ `max-age = min`, phase `Hold`.

**Intent:** cutover time has arrived (or passed) but config still points at the old schedule. Clients keep revalidating often until you:

1. Publish new data / bump `Version`, and  
2. Set the **next** `ScheduledUpdateUtc` (and usually leave hold enabled for the new version).

### 4. Calm

If `secondsToSchedule = (ScheduledUpdateUtc - now).TotalSeconds`  
and `secondsToSchedule >= max`:

→ `max-age = max`, phase `Calm`.

### 5. Ramp (Approaching)

Otherwise `min ≤ secondsToSchedule < max` (after clamp of `t`):

```
t     = clamp(secondsToSchedule, min, max)
maxAge = round( min + (max - min) * (t - min) / (max - min) )
```

So:

- At the start of the window (`t ≈ max`): near **max**  
- At the end (`t ≈ min`): **min**  
- Linear in between  

Phase `Approaching`.  
If `ClientMustRevalidateNearUpdate` and `maxAge <= min` → append `must-revalidate`.

**Hold also appends `must-revalidate`** when that flag is on (not only the Approaching floor). Tests: after `ScheduledUpdateUtc` with the flag, the header includes `must-revalidate`.

---

## Safety valves

### `ClientTtlMinSeconds` — the floor

Never let the schedule (or hold) drive client TTL to zero or to an unusable flicker. The floor is:

- End of the ramp  
- All of **Hold**  

Pick a min that is short enough that a bad cutover is not sticky for days, but long enough that you do not DDoS origin (e.g. 5–15 minutes for heavy assets, 30–60s for APIs).

### `ClientMustRevalidateNearUpdate`

Optional HTTP hint: once at the floor, add `must-revalidate` so caches that honor it revalidate with the origin when the entry is stale (instead of serving stale under some heuristics).

---

## Operational playbook

### Planned cutover

1. Set `ClientCache.ScheduledUpdateUtc` to the planned go-live (days/weeks ahead).  
2. Keep `ClientCache.TtlSeconds` large; set a sensible `ClientCache.TtlMinSeconds`.  
3. Optionally enable `ClientCache.MustRevalidateNearUpdate`.  
4. At go-live: deploy content, bump **`Version`**, set **next** `ScheduledUpdateUtc`.  
5. Watch traffic/errors; long client cache resumes.

### No planned date

Omit `ScheduledUpdateUtc`. Clients always get `ClientCache.TtlSeconds` (or NoStore). Server invalidation still works via `Version` / tag purge.

### Interaction with server caches

| Layer | Controlled by |
|-------|----------------|
| Browser / shared CDN client cache | **Client Cache Schedule** (`ClientCache` / `Cache-Control`) |
| ASP.NET Output Cache | `OutputCache.TtlSeconds` (often shorter than client max) |
| Data cache (Fusion L1/L2) | `DataCache.TtlSeconds` + optional `fusionCache.hardTtlSeconds` / `FailSafeSeconds` |

A common pattern: **server TTL short**, **client TTL long but scheduled**—origin is protected by Output Cache and the data cache, while public clients still get long calm periods and timely cutover refresh.

---

## Edge cases

| Case | Behaviour |
|------|-----------|
| `ClientTtlMinSeconds > ClientTtlSeconds` | Min is clamped down to max |
| `max == min` | No visible ramp; always that value in the window |
| Clock skew | Use UTC everywhere; rely on `TimeProvider` in the host |

---

## Code map

| Type | Role |
|------|------|
| `ClientCacheHeaderGenerator.Build` | Pure function: options + `now` → header + phase |
| `ClientCacheSchedulePhase` | Calm / Approaching / Hold / NotApplicable |
| `DomainOutputCachePolicy` | Calls `Build` on response start with `TimeProvider.GetUtcNow()` |
| Domain options fields | Bound from config; see [configuration.md](configuration.md) |

---

## Related

- [Guide — concepts](guide/concepts.md)  
- [configuration.md](configuration.md) — full property list  
- [output-cache.md](output-cache.md) — where headers are applied  
- [invalidation.md](invalidation.md) — `Version` for server keys vs client schedule  
