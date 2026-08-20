# Client Cache Schedule

> **Guide.** Product overview: [root README](../README.md). Orientation: [Guide — concepts](guide/concepts.md). Catalog: [documentation index](README.md). Canonical algorithm and settings are on this page.

Browsers and CDNs keep a **long `max-age`** for most of the life of a dataset, then that lifetime **falls toward a floor** as a planned cutover (`ScheduledUpdateUtc`) approaches. After the cutover they stay on the floor until you open the next window. Clients revalidate in time for the new generation, without a tiny `max-age` all month.

This changes only the **client** `Cache-Control` header. Output Cache and FusionCache keep their own TTLs (`OutputCacheTtlSeconds`, `FusionCacheSoftTtlSeconds`, …).

The playground sample shows the phases live. Implementation: `ClientCacheHeaderGenerator`, `ClientCacheSchedulePhase`.

---

## Why it exists

Classic problem with long-lived client caches:

- You want `max-age` of hours/days for performance and cost.  
- You also have a **known go-live** (map tile batch, catalog cutover, CMS publish).  
- If clients still hold a 7-day `max-age` at cutover, they stay on **stale content** until that window expires—even if the origin is already updated.

Client Cache Schedule solves that by **aligning client revalidation with the cutover clock**, without forcing a tiny `max-age` all month long.

```text
max-age
    ▲
max │─── CALM ───●                                        ┌─── CALM ───
    │             \                                       │
    │          APPROACHING                                │
    │               \                                     │
min │                ●────────────── HOLD ────────────────┘
    └────────────────┬────────────────┬───────────────────┬──────────► time
            ScheduledUpdateUtc     Version             New ScheduledUpdateUtc 
                                   Changed             Defined or Omitted
```

1. **SU (ScheduledUpdateUtc)**: The target time when the schedule is reached. The cache `max-age` drops to `ClientTtlMinSeconds`.
2. **Version Changed**: The actual deployment happens (e.g. `Version` string is updated). The `max-age` remains at `ClientTtlMinSeconds` because `ScheduledUpdateUtc` is still in the past. This gives you a safe "hold" period to monitor the release with a short cache TTL.
3. **New SU Defined or Omitted**: When you are satisfied with the release, you either remove `ScheduledUpdateUtc` (set to `null`) or set it to a future date. The `max-age` immediately jumps back up to the long `ClientTtlSeconds` (CALM phase).

---

## Observability

Every eligible response that goes through the Output Cache header path reports the phase:

- **X-Cache** — `phase=calm`, `phase=approaching`, `phase=hold`, or `phase=n/a`.
- **Metrics** — counter `cache_orchestrator.client.schedule` with tags `domain` and `phase` (same strings).

Meter name: `CacheOrchestrator`. See [observability.md](observability.md).

## Settings

All under `Cache:DomainDefaults` / `Cache:Domains:{name}`:

- **ClientCacheability** — `Public`, `Private`, or `NoStore`. `NoStore` turns the schedule off.
- **ClientTtlSeconds** — target `max-age` when far from cutover, and when there is no schedule.
- **ClientTtlMinSeconds** — floor `max-age` near cutover, after cutover, and during the hold after a Version change.
- **ScheduledUpdateUtc** — planned cutover (UTC). Omit it for a constant `ClientTtlSeconds`.
- **ClientMustRevalidateNearUpdate** — at the floor, append `must-revalidate`.
- **Version** — generation stamp (also used for server keys and ETag).

### Example (map tiles / periodic dataset)

```json
"maps-osm": {
  "Version": "v1",
  "ScheduledUpdateUtc": "2026-12-01T00:00:00Z",
  "ClientCacheability": "Public",
  "ClientTtlSeconds": 2592000,
  "ClientTtlMinSeconds": 900,
  "ClientMustRevalidateNearUpdate": true,
  "OutputCacheTtlSeconds": 300,
  "FusionCacheSoftTtlSeconds": 3600
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
| **Calm** | `calm` | `secondsUntilSchedule >= ClientTtlSeconds` | `ClientTtlSeconds` (max) |
| **Approaching** | `approaching` | Inside the ramp window before cutover | Linear between max and min |
| **Hold** | `hold` | `now >= ScheduledUpdateUtc` | `ClientTtlMinSeconds` (min) |
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

1. Set `ScheduledUpdateUtc` to the planned go-live (days/weeks ahead).  
2. Keep `ClientTtlSeconds` large; set a sensible `ClientTtlMinSeconds`.  
3. Optionally enable `ClientMustRevalidateNearUpdate`.  
4. At go-live: deploy content, bump **`Version`**, set **next** `ScheduledUpdateUtc`.  
5. Watch traffic/errors; long client cache resumes.

### No planned date

Omit `ScheduledUpdateUtc`. Clients always get `ClientTtlSeconds` (or NoStore). Server invalidation still works via `Version` / tag purge.

### Interaction with server caches

| Layer | Controlled by |
|-------|----------------|
| Browser / shared CDN client cache | **Client Cache Schedule** (`Cache-Control`) |
| ASP.NET Output Cache | `OutputCacheTtlSeconds` (often shorter than client max) |
| FusionCache L1/L2 | soft/hard/fail-safe seconds |

A common pattern: **server TTL short**, **client TTL long but scheduled**—origin is protected by Output/Fusion, while public clients still get long calm periods and timely cutover refresh.

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
