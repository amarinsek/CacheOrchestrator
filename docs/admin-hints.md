# Admin recommendation hints

How the Admin App turns live counters (and, for some rules, domain config) into **read-only recommendations**. Hints leave cache behaviour unchanged. This page is for adding or changing a rule.

---

## Where the logic lives

| Layer | Location | Role |
|-------|----------|------|
| **Rule engine** | `src/CacheOrchestrator.Admin/Services/RecommendationHints.cs` | All conditions and messages |
| **DTO shape** | `src/CacheOrchestrator/Admin/AdminDtos.cs` → `AdminHintDto`, `AdminHintSummaryDto` | Wire format shared with Admin API types |
| **Attachment** | `AdminFanOutService` (after stats aggregation) | Calls `WithHints` on domain/endpoint rows |
| **Share / rate math** | `src/CacheOrchestrator/Admin/AdminStatsMath.cs` | `HitRate`, `HitShare`, `OriginShare`, `StaleShare`, … |
| **UI** | `src/CacheOrchestrator.Admin/wwwroot/js/hints.js` | Badges, severity stack, Hints page flatten — **no rules** |

Flow:

```
Admin API /stats  →  fan-out aggregate (StatsAggregator)
                    →  RecommendationHints.WithHints(domain, config?)
                    →  entity.Hints[] + cluster HintSummary
                    →  SPA (badges / Hints page)
```

Rules run **only in the Admin App**, on aggregated (or per-instance) stats — not inside each app’s hot path.

---

## Hint object model

```csharp
// AdminHintDto
Severity  // "Info" | "Warning" | "Critical"
Code      // stable machine id, kebab-case, e.g. "high-origin-share"
Message   // human text (may include live numbers)
```

`AdminHintSummaryDto` rolls up counts (`Info` / `Warning` / `Critical`) and exposes `MaxSeverity` for header chips.

**Severity usage today**

| Severity | Typical meaning |
|----------|-----------------|
| `Critical` | Reserved for urgent cluster issues (few/no rules use it yet) |
| `Warning` | Actionable performance / consistency concern |
| `Info` | Config smell or metric interpretation tip |

---

## How a rule is defined (formula pattern)

Each rule is an **if condition → emit Hint(severity, code, message)** inside:

- `ForDomain(AdminDomainStatsDto domain, AdminDomainConfigDto? config)` — domain-level (+ optional config)
- `ForEndpoint(AdminEndpointStatsDto ep)` — route-level

Common building blocks:

| Symbol | Source | Meaning |
|--------|--------|---------|
| `R` | `domain.Requests` / `ep.Requests` | Request denominator for shares |
| `MinTraffic` | `RecommendationHints.MinTraffic` (= **20**) | Gate: most rate rules need `R >= 20` |
| Layer **hit rate** | `Oc.HitRate` / `Fc.HitRate` | `hits / (hits + misses)` **within the layer** |
| Request **share** | `Oc.HitShare`, `Fc.OriginShare`, `Fc.StaleShare`, … | `count / total requests` |
| Config | `AdminDomainConfigDto` | TTLs, schedule phase (when fan-out loads config) |
| Spread | `InstanceSpread.*.Stdev` | Cross-instance heterogeneity (when `groupByInstance`) |

**Shares vs rates (important for formulas)**

- Prefer **shares of requests** for “how much of traffic is origin?” → `Fc.OriginShare`.
- **Layer rates** (`Fc.HitRate`) only describe traffic that reached Fusion; with high OC hit share they can look alarming while origin share is low. Rule `fc-miss-rate-vs-oc-share` teaches that distinction.

Helpers when attaching:

```csharp
// Clone DTO and set Hints (and recurse endpoints / byInstance)
RecommendationHints.WithHints(domain, config);
RecommendationHints.WithHints(endpoint);

// Flatten for overview / summary
RecommendationHints.CollectFromStats(domains, endpoints);
RecommendationHints.Summarize(hints);
```

Private factory:

```csharp
private static AdminHintDto Hint(string severity, string code, string message) =>
    new() { Severity = severity, Code = code, Message = message };
```

---

## Catalogue of existing rules

Constants: `MinTraffic = 20`. Thresholds below are as implemented (not config-driven today).

### Domain — `ForDomain` (needs `R >= MinTraffic` unless noted)

| Code | Severity | Condition |
|------|----------|-----------|
| `low-fc-hit-rate` | Warning | `R >= 20` and `Fc.HitRate < 0.60` |
| `low-oc-hit-rate` | Warning | `R >= 20` and `Oc.HitRate < 0.60` |
| `high-origin-share` | Warning | `R >= 20` and `Fc.OriginShare >= 0.25` |
| `elevated-stale` | Warning | `R >= 20` and `Fc.StaleShare >= 0.05` |
| `very-high-oc-hit-long-ttl` | Info | `R >= 20` and `Oc.HitRate >= 0.98` and `config.OutputCacheTtlSeconds >= 3600` |
| `frequent-invalidations` | Info | `R >= 20` and `Invalidations >= 10` and `Invalidations >= 0.05 * R` |
| `client-ttl-gt-output` | Info | config: `ClientTtlSeconds > 0` and `OutputCacheTtlSeconds > 0` and `ClientTtlSeconds > 2 * OutputCacheTtlSeconds` |
| `schedule-phase` | Info | config: `SchedulePhase` is `hold` or `approaching` |
| `instance-oc-hit-spread` | Warning | `InstanceSpread.OcHitShare.SampleCount >= 2` and `Stdev >= 0.15` (no MinTraffic gate) |

### Endpoint — `ForEndpoint` (needs `R >= MinTraffic`)

| Code | Severity | Condition |
|------|----------|-----------|
| `low-fc-hit-rate` | Warning | `R >= 20` and `Fc.HitRate < 0.60` and `Fc.LayerSampleSize >= 10` |
| `high-origin-share` | Warning | `R >= 20` and `Fc.OriginShare >= 0.25` |
| `elevated-stale` | Warning | `R >= 20` and `Fc.Stale > 0` and `Fc.StaleShare >= 0.05` |
| `fc-miss-rate-vs-oc-share` | Info | `R >= 20` and `Oc.HitShare >= 0.95` and `Fc.MissRate >= 0.99` and `0 < Fc.LayerSampleSize < LowSampleThreshold` |
| `instance-origin-spread` | Warning | `R >= 20` and `InstanceSpread.OriginShare.SampleCount >= 2` and `Stdev >= 0.15` |

Same `code` may appear on domain and endpoint with different messages; the Hints page dedupes by `Severity|Code|Message`.

---

## Worked examples (existing)

### 1. `high-origin-share` (domain)

**Intent:** Too many requests run the Fusion factory / origin path.

**Condition:**

```text
R >= 20 and OriginShare >= 0.25
  where OriginShare = FactoryRuns / R   (request share, not layer rate)
```

**Code** (`RecommendationHints.ForDomain`):

```csharp
if (domain.Requests >= MinTraffic)
{
    if (domain.Fc.OriginShare is double origin && origin >= 0.25)
    {
        hints.Add(Hint(
            "Warning",
            "high-origin-share",
            $"Origin/factory share {(origin * 100):0.#}% of requests — …"));
    }
}
```

**UI:** Warning badge on domain rows; message on domain detail and Hints page.

### 2. `low-fc-hit-rate` (endpoint)

**Intent:** Among Fusion layer traffic, hits are weak — TTL or key cardinality issues on this route.

**Condition:**

```text
R >= 20
and Fc.HitRate < 0.60
and Fc.LayerSampleSize >= 10

where Fc.HitRate = hits / (hits + misses) on the FC layer
  (not of all HTTP requests)
```

**Why extra sample gate:** Avoid noisy hints when almost all traffic was OC-hit and FC barely ran.

---

## Adding a new hint (recipe)

### Step 1 — Choose scope

- Domain-wide signal (TTL, invalidations, schedule) → `ForDomain`.
- Route-specific signal → `ForEndpoint`.
- Needs config (TTL seconds, schedule) → `ForDomain` + non-null `config` (fan-out already passes config when available).

### Step 2 — Define formula

Write a clear predicate using **shares** when possible, and gate with `MinTraffic` unless the rule is config-only or spread-only.

Example: **Critical** when origin share is extreme:

```text
R >= 20 and OriginShare >= 0.50
```

### Step 3 — Implement in `RecommendationHints.cs`

```csharp
// Inside ForDomain, inside the MinTraffic block (or after, if config-only):

if (domain.Fc.OriginShare is double originCrit && originCrit >= 0.50)
{
    hints.Add(Hint(
        "Critical",
        "critical-origin-share",
        $"Origin share {(originCrit * 100):0.#}% with {domain.Requests} requests — " +
        "factory path dominates; raise soft/hard TTL, enable fail-safe, or reduce key churn."));
}
```

Conventions:

| Item | Rule |
|------|------|
| `code` | kebab-case, unique intent (`critical-origin-share`) |
| `severity` | `Info` / `Warning` / `Critical` |
| Message | Include live metric values; actionable one-liner |
| Thresholds | Prefer named `const` if reused across rules |

### Step 4 — Optional UI short label

In `wwwroot/js/hints.js` → `shortHint()` map, add a compact badge label:

```js
"critical-origin-share": "Origin‼",
```

Without this, the badge falls back to the first characters of severity.

### Step 5 — Verify

1. Build Admin App; hit Overview / Domains / Endpoints with traffic that satisfies the formula.  
2. Confirm badge + detail list + Hints page row.  
3. Unit-test style (optional): pure static methods — feed a fabricated `AdminDomainStatsDto` into `ForDomain` and assert `Code` / `Severity`.

No SPA rule logic and no Admin API changes are required unless you need new counters not already on the stats DTO.

---

## Checklist for a good rule

1. **Gated** — enough samples (`MinTraffic` or layer sample size).  
2. **Metric choice** — share of requests vs layer rate is intentional.  
3. **Stable `code`** — clients and future docs can refer to it.  
4. **Actionable message** — what to check (TTL, keys, invalidation, schedule, …).  
5. **Severity honest** — Info ≠ Warning; reserve Critical for “fix now”.  
6. **No side effects** — hints never write version/TTL/invalidate.

---

## Related

- Admin App overview: [admin.md](admin.md)  
- Operator README: [src/CacheOrchestrator.Admin/README.md](../src/CacheOrchestrator.Admin/README.md)  
- Stats math: `AdminStatsMath` (shares, rates, low-sample threshold)  
- Implementation: `RecommendationHints.cs`  
