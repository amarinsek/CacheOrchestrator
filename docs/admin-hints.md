# Admin recommendation hints

How the Admin App turns **live counters** (and domain config) into **read-only recommendations**.  
Hints never change cache behaviour, TTLs, or invalidation.

**Customization is first-class.** Product defaults ship as JSON; operators add packs and can disable any code from Settings—without recompiling the Admin App.

| Document | Audience |
|----------|----------|
| **[Operator guide: writing rules](../src/CacheOrchestrator.Admin/hints/README.md)** | Full how-to, JSON format, paths, step-by-step new rule (**ships with the Admin App** next to the packs) |
| [Admin App README](../src/CacheOrchestrator.Admin/README.md) | Run/configure the host + feature overview |
| [admin.md](admin.md) | Admin architecture / security |

---

## What operators need

1. Open Admin → **Settings** for the rule catalog, compile errors, enable/disable, and “view rule JSON”.  
2. Open **Hints** / domain / endpoint pages for live recommendations.  
3. To **add a rule**: write a JSON pack under `hints/`, load it via `CacheAdmin:Hints:RuleFiles`, Reload — details in the **[operator guide](../src/CacheOrchestrator.Admin/hints/README.md)**.

---

## Architecture (repository)

```
Local Admin /stats  →  Admin App fan-out (StatsAggregator)
                    →  HintEngine (JSON rules)
                    →  entity.Hints[] + HintSummary
                    →  SPA (badges, Hints page, Settings)
```

| Piece | Role |
|-------|------|
| `hints/core-hints.json` | Product defaults (**always** loaded) |
| `CacheAdmin:Hints:RuleFiles` | Extra operator packs (globs) |
| `HintEngine` / `IHintRule` | Evaluation |
| `HintEvaluationContext` | Read-only facts + computed fields |
| Compiler | Validate packs; errors include **rule code** + path *inside* the rule |
| Settings UI (`#/settings`) | Catalog by file, disable, inspect JSON, reload |
| `wwwroot/js/hints.js` | Presentation only |

Rules run **only in the Admin App**, not on each instance’s caching hot path.

**Runtime evaluation is declarative JSON.** `RecommendationHints.cs` remains as a reference/unit-test helper for some legacy formulas; new product rules belong in `core-hints.json` or operator packs.

---

## Config (summary)

```json
"CacheAdmin": {
  "Hints": {
    "RuleFiles": [ "hints/*.json" ],
    "DisabledCodes": [],
    "DisabledStatePath": "hints/disabled.local.json"
  }
}
```

| Key | Meaning |
|-----|---------|
| `RuleFiles` | Paths/globs under Admin content root (`core-hints.json` always loads) |
| `DisabledCodes` | Shared disable list (safe to commit per environment) |
| `DisabledStatePath` | Settings UI toggles — **local file, gitignored** |

Load order: **core pack**, then `RuleFiles` (skip `disabled.local.json`, `*.sample.json`, duplicates).  
Uniqueness: **`(code, scope)`** — same code may exist for domain and endpoint.

Full disable options and pack examples: [hints/README.md](../src/CacheOrchestrator.Admin/hints/README.md).

---

## Severity (product meaning)

| Level | Meaning |
|-------|---------|
| **Critical** | Pipeline badly wrong (e.g. origin ≥ 50%, factory mostly failing) |
| **Warning** | Fault worth fixing (high origin, stale covering failures, drift, hard &lt; soft TTL, lingering hold) |
| **Info** | Operational note (approaching cutover, recent hold, runtime overlay, frequent invalidations) |

**Origin share** in Admin is the same as **Fusion factory share**: `factoryRuns / requests` (CDN “origin” = factory miss path). Prefer **origin share** / factory failure rate over raw Fusion layer hit rate. A 0% FC layer rate with low origin is often normal when Output Cache absorbs traffic.

---

## Product core codes (overview)

Shipped in `core-hints.json` (domain and/or endpoint scope as applicable):

| Codes | Theme |
|-------|--------|
| `high-origin-share`, `critical-origin-share` | Origin / factory share |
| `elevated-stale` | Fail-safe stale share |
| `factory-failures`, `critical-factory-failures` | Factory error rate |
| `frequent-invalidations` | Invalidation vs traffic |
| `schedule-approaching`, `schedule-phase`, `schedule-hold-lingering` | Client Cache Schedule |
| `client-ttl-gt-output`, `schedule-flat` | Client TTL / ramp |
| `fusion-hard-lt-soft` | Fusion soft vs hard |
| `runtime-override` | Runtime Version/TTL overlay |
| `instance-oc-hit-spread`, `instance-origin-spread` | Cross-instance drift |

Exact thresholds and messages: open **`core-hints.json`** or Settings → click a row.

---

## API (Admin App)

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/hints/rules` | Catalog, load status, known paths |
| POST | `/api/hints/reload` | Reload packs without restart |
| PUT | `/api/hints/rules/{code}/enabled` | `{ "enabled": true/false }` |

---

## Implementation map (contributors)

| Area | Location under `src/CacheOrchestrator.Admin/` |
|------|-----------------------------------------------|
| Engine | `Services/Hints/` |
| Declarative compiler / conditions | `Services/Hints/Declarative/` |
| Disable store | `Services/Hints/HintRuleDisableStore.cs` |
| Product + operator packs | `hints/` |
| Settings UI | `wwwroot/js/views.js` (`renderSettingsPage`) |
| Attachment after fan-out | `Services/AdminFanOutService.cs` |

---

## See also

- **[Writing rules (distributed with Admin)](../src/CacheOrchestrator.Admin/hints/README.md)**  
- [Admin App README](../src/CacheOrchestrator.Admin/README.md)  
- [admin.md](admin.md)  
- [client-cache-schedule.md](client-cache-schedule.md)  
