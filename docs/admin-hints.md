# Admin recommendation hints

> **Reference.** Product overview: [root README](../README.md). Orientation: [Guide — operations](guide/operations.md). Catalog: [documentation index](README.md). Repo architecture for hints; **how to write rules:** [hints/README.md](../src/CacheOrchestrator.AdminConsole/hints/README.md).

How the Admin Console App turns **live counters** (and domain config) into **read-only recommendations**.  
Hints never change cache behaviour, TTLs, or invalidation.

**Customization is first-class.** Product defaults ship as JSON; operators add packs and can disable any code from Settings—without recompiling the Admin Console App.

| Document | Audience |
|----------|----------|
| [Guide — operations](guide/operations.md) | Which document to open |
| **[Operator guide: writing rules](../src/CacheOrchestrator.AdminConsole/hints/README.md)** | Full how-to, JSON format, paths, step-by-step new rule (**ships with the Admin Console App** next to the packs) |
| [Admin Console App README](../src/CacheOrchestrator.AdminConsole/README.md) | Run/configure the host + feature overview |
| [admin.md](admin.md) | Admin architecture / security |
| [deploy/admin/README.md](../deploy/admin/README.md) | Docker / GHCR / volumes |

---

## What operators need

1. Open Admin → **Settings** for the rule catalog, compile errors, enable/disable, and “view rule JSON”.  
2. Open **Hints** / domain / endpoint pages for live recommendations.  
3. To **add a rule**: write a JSON pack (`hints/` in Development, or `data/rules/` in Docker/Production), load it via `AdminConsole:Hints:RuleFiles`, Reload — details in the **[operator guide](../src/CacheOrchestrator.AdminConsole/hints/README.md)**. Docker volume layout: [deploy/admin/README.md](../deploy/admin/README.md).

---

## Architecture (repository)

```
Prometheus (OTEL meter)  →  Admin Console /api/stats/window  (Range)
                         →  Admin Console /api/live          (fixed 1m)
                         →  HintEngine (JSON rules)
                         →  entity.Hints[] + HintSummary
                         →  SPA (badges, Hints page, Live, Settings)

Domain config (optional) →  Admin fan-out /api/domains  →  config-only rules
```

| Piece | Role |
|-------|------|
| `hints/core-hints.json` | Product defaults (**always** loaded) |
| `AdminConsole:Hints:RuleFiles` | Extra operator packs (globs) |
| `HintEngine` / `IHintRule` | Evaluation |
| `HintEvaluationContext` | Read-only facts + computed fields |
| Compiler | Validate packs; errors include **rule code** + path *inside* the rule |
| Settings UI (`#/settings`) | Catalog by file, disable, inspect JSON, reload |
| `wwwroot/js/hints.js` | Presentation only |

Rules run **only in the Admin Console App**, not on each instance’s caching hot path.

**Runtime evaluation is declarative JSON** via `HintEngine` + `hints/core-hints.json` (and optional operator packs). Product rules belong in JSON packs, not C# helpers.

---

## Config (summary)

```json
"AdminConsole": {
  "Hints": {
    "RuleFiles": [ "hints/*.json" ],
    "DisabledCodes": [],
    "DisabledStatePath": "hints/disabled.local.json"
  }
}
```

Production / Docker defaults: `data/rules/*.json` and `data/disabled.local.json` (mount `/app/data`). Development keeps the `hints/` paths above.

| Key | Meaning |
|-----|---------|
| `RuleFiles` | Paths/globs under Admin content root (`core-hints.json` always loads) |
| `DisabledCodes` | Shared disable list (safe to commit per environment) |
| `DisabledStatePath` | Settings UI toggles — **local file, gitignored** |

Load order: **core pack**, then `RuleFiles` (skip `disabled.local.json`, `*.sample.json`, duplicates).  
Uniqueness: **`(code, scope)`** — same code may exist for domain and endpoint.

Full disable options and pack examples: [hints/README.md](../src/CacheOrchestrator.AdminConsole/hints/README.md).

---

## Severity (product meaning)

| Level | Meaning |
|-------|---------|
| **Critical** | Pipeline badly wrong (e.g. factory share ≥ 50%, factory mostly failing) |
| **Warning** | Fault worth fixing (high factory share, stale covering failures, drift, hard &lt; soft TTL, lingering hold) |
| **Info** | Operational note (approaching cutover, recent hold, runtime overlay, frequent invalidations) |

**Factory share** in Admin is `factoryRuns / requests` (API: `factoryShare`; obsolete synonym `originShare`). **Factory** is also known as **origin** in CDN terms. Prefer factory share / factory failure rate over raw Fusion layer hit rate. A 0% FC layer rate with low factory share is often normal when Output Cache absorbs traffic.

---

## Product core codes (overview)

Shipped in `core-hints.json` (domain and/or endpoint scope as applicable):

| Codes | Theme |
|-------|--------|
| `high-factory-share`, `critical-factory-share` | Factory share (API: `factoryShare`) |
| `impact-poor-candidate`, `impact-at-risk`, `impact-strong` | Domain impact (`domain.impact.*`) |
| `endpoint-impact-poor-candidate` | Endpoint impact (`endpoint.impact.*`) |
| `elevated-stale` | Fail-safe stale share |
| `factory-failures`, `critical-factory-failures` | Factory error rate |
| `frequent-invalidations` | Invalidation vs traffic |
| `schedule-approaching`, `schedule-phase`, `schedule-hold-lingering` | Client Cache Schedule |
| `client-ttl-gt-output`, `schedule-flat` | Client TTL / ramp |
| `fusion-hard-lt-soft` | Fusion soft vs hard |
| `runtime-override` | Runtime Version/TTL overlay |
| `instance-oc-hit-spread`, `instance-factory-spread` | Cross-instance OC / factory share drift |

Exact thresholds and messages: open **`core-hints.json`** or Settings → click a row.

---

## API (Admin Console App)

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/hints/rules` | Catalog, load status, known paths |
| POST | `/api/hints/reload` | Reload packs without restart |
| PUT | `/api/hints/rules/{code}/enabled` | `{ "enabled": true/false }` |

---

## Implementation map (contributors)

| Area | Location under `src/CacheOrchestrator.AdminConsole/` |
|------|-----------------------------------------------|
| Engine | `Services/Hints/` |
| Declarative compiler / conditions | `Services/Hints/Declarative/` |
| Disable store | `Services/Hints/HintRuleDisableStore.cs` |
| Product + operator packs | `hints/` |
| Console DTOs (SPA / fan-out) | `Models/` (`OverviewDtos`, `FanOutDtos`, `WriteRequestDtos`, …) |
| Settings UI | `wwwroot/js/views-settings.js` (`renderSettingsPage`; routed from thin `views.js`) |
| Attachment on window stats | `Services/Metrics/MetricsWindowStatsService.cs` (`HintEngine`) |

---

## See also

- [Guide — operations](guide/operations.md)  
- **[Writing rules (distributed with Admin)](../src/CacheOrchestrator.AdminConsole/hints/README.md)**  
- [Admin Console App README](../src/CacheOrchestrator.AdminConsole/README.md)  
- [admin.md](admin.md)  
- [client-cache-schedule.md](client-cache-schedule.md)  
