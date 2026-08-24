# Client Cache Schedule

> **Guide.** Product overview: [root README](../../README.md). Orientation: [concepts](concepts.md). Catalog: [documentation index](../README.md). Exact algorithm: [reference](../reference/client-cache-schedule-algorithm.md).

Large datasets that update on a published schedule (annual satellite imagery, monthly catalog extracts) create a client-caching dilemma.

You want a **long** browser/CDN `max-age` for most of the year. But if a client caches for thirty days the day before cutover, it keeps serving the old generation long after origin has moved on.

**Client Cache Schedule** solves that. Given `ScheduledUpdateUtc`, it keeps a long `max-age` while far from cutover, then **ramps down** toward a short floor as the date approaches, then **holds** at the floor until you set the next schedule. Clients are primed to refresh when the new generation lands — without living on a tiny TTL all year.

This changes only the **client** `Cache-Control` header. Output Cache and the data cache keep their own TTLs.

The [playground sample](../../samples/CacheOrchestrator.Sample) shows the phases live.

---

## How it works

On each eligible response, CacheOrchestrator looks at the time remaining until `ScheduledUpdateUtc` and chooses `max-age` so no client is told to keep a copy past the planned cutover.

<img src="../assets/scheduled-update.svg" height="350" />

1. **Calm** — far from cutover: full `ClientCache.TtlSeconds`.
2. **Approaching** — inside the ramp window: `max-age` shortens linearly toward `ClientCache.TtlMinSeconds`.
3. **Hold** — at or after cutover: stay at the floor so operators can deploy and verify while clients recover quickly.
4. **Reset** — bump `Version`, set the **next** `ScheduledUpdateUtc` (or clear it): long calm resumes.

---

## Settings

Under `Cache:DomainDefaults` / `Cache:Domains:{name}`, nested **`ClientCache`**:

| Setting | Role |
|---------|------|
| `Cacheability` | `Public`, `Private`, or `NoStore` (`NoStore` disables the schedule) |
| `TtlSeconds` | Target `max-age` when calm (and when there is no schedule) |
| `TtlMinSeconds` | Floor near cutover and during Hold |
| `ScheduledUpdateUtc` | Planned cutover (UTC). Omit for a constant client TTL |
| `MustRevalidateNearUpdate` | At the floor, append `must-revalidate` |

Server layers are independent: `OutputCache.TtlSeconds`, `DataCache.TtlSeconds`, and optional Fusion `HardTtlSeconds` / `FailSafeSeconds`.

### Example

```json
"maps-satellite": {
  "Version": "v1",
  "DataCache": { "TtlSeconds": 3600 },
  "OutputCache": { "TtlSeconds": 300 },
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
- Inside the last 30 days: `max-age` **ramps down** toward **15 minutes**.
- After cutover (until you set a new schedule/version): stay at **15 minutes**.

---

## Phases

| Phase | `X-Cache` / metrics | When | Client `max-age` |
|-------|---------------------|------|------------------|
| **Calm** | `calm` | Far enough from cutover | `TtlSeconds` |
| **Approaching** | `approaching` | Inside the ramp window | Linear between max and min |
| **Hold** | `hold` | `now >= ScheduledUpdateUtc` | `TtlMinSeconds` |
| **NotApplicable** | `n/a` | NoStore, blocked, or no schedule | Constant max or `no-store` |

Exact ramp math: [algorithm](../reference/client-cache-schedule-algorithm.md).

---

## Observability

- **`X-Cache`** — `phase=calm|approaching|hold|n/a`
- **Metrics** — `cache_orchestrator.client.schedule` with tags `domain`, `phase`

Meter name: `CacheOrchestrator`. See [observability](../reference/observability.md).

---

## Operational playbook

### Planned cutover

1. Set `ScheduledUpdateUtc` days/weeks ahead.
2. Keep `TtlSeconds` large; pick a sensible `TtlMinSeconds` (e.g. 5–15 minutes for heavy assets).
3. Optionally enable `MustRevalidateNearUpdate`.
4. At go-live: deploy content, bump **`Version`**, set the **next** `ScheduledUpdateUtc`.
5. Watch traffic; long client cache resumes.

### No planned date

Omit `ScheduledUpdateUtc`. Clients always get `TtlSeconds` (or NoStore). Server freshness still uses Version / tag purge.

### Server vs client TTLs

| Layer | Controlled by |
|-------|----------------|
| Browser / CDN | Client Cache Schedule (`ClientCache`) |
| ASP.NET Output Cache | `OutputCache.TtlSeconds` (often shorter than client calm) |
| Data cache | `DataCache.TtlSeconds` (+ Fusion knobs when using Fusion) |

A common pattern: **server TTL shorter**, **client TTL long but scheduled** — origin stays protected while public clients still get long calm periods.

---

## Edge cases

| Case | Behaviour |
|------|-----------|
| `TtlMinSeconds > TtlSeconds` | Min is clamped down to max |
| `max == min` | No visible ramp |
| Clock skew | Use UTC; rely on host `TimeProvider` |

---

## Related

- [Exact algorithm](../reference/client-cache-schedule-algorithm.md)
- [Concepts](concepts.md)
- [Domain profiles](domain-profiles.md) — snapshot cutovers
- [Configuration](../reference/configuration.md)
- [Observability](../reference/observability.md)
