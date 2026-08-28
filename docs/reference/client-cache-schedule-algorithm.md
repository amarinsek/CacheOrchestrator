# Client Cache Schedule algorithm

> **Reference.** Product overview: [root README](../../README.md). Catalog: [documentation index](../README.md). Playbook: [Client Cache Schedule](../guide/client-cache-schedule.md).

Exact phase and `max-age` rules used by `ClientCacheHeaderGenerator.Build`. For when to use the feature and how to operate cutovers, see the [guide](../guide/client-cache-schedule.md).

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

If `now >= ScheduledUpdateUtc`:

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
If runtime option `ClientMustRevalidateNearUpdate` is `true` and `maxAge <= min` → append `must-revalidate`. Its configuration source is `ClientCache.MustRevalidateNearUpdate`.

**Hold also appends `must-revalidate`** when that flag is on (not only the Approaching floor). Tests: after `ScheduledUpdateUtc` with the flag, the header includes `must-revalidate`.

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

- [Client Cache Schedule (guide)](../guide/client-cache-schedule.md)
- [configuration.md](configuration.md) — `ClientCache` properties
- [Output Cache](output-cache.md) — where headers are applied
- [observability.md](observability.md) — `phase=` on `X-Cache`
