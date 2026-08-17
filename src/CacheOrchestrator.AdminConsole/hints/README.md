# Writing Admin hint rules

This folder ships with the **CacheOrchestrator Admin Console App**. It holds:

| File | Role |
|------|------|
| **`core-hints.json`** | Product default rules (**always** loaded, every environment) |
| **`*.json`** (your packs) | Custom rules when listed in `RuleFiles` (Development default: this folder) |
| **`disabled.local.json`** | Enable/disable from the Settings UI (**do not commit**; machine-local) |

Hints are **read-only recommendations** built from the Console **derived** stats model (and domain config), not from raw instance JSON. After `/stats/v2` fan-out the Console computes shares and **impact** (`domain.impact.*` / `endpoint.impact.*`) before rules run. They never change TTLs, Version, or invalidation.

You can add rules **without recompiling** the Admin Console App: drop a JSON file where `RuleFiles` points, then **Settings → Reload**.

### Development vs Docker / Production

| | Custom packs | Disabled state |
|--|--------------|----------------|
| **Development** (`dotnet run`) | `hints/*.json` (this folder) | `hints/disabled.local.json` |
| **Production / container** | `data/rules/*.json` (mount a volume over `/app/data`) | `data/disabled.local.json` |

In Docker, keep **`core-hints.json` in the image** (do not mount over all of `/app/hints`). Put only your packs under the operator `data/` volume. Step-by-step: [deploy/admin/README.md](../../../deploy/admin/README.md).

---

## Why customize

Default rules cover common factory-share (also known as origin), **impact** (poor candidate / at-risk / strong), factory failure, schedule, and TTL problems. Teams often need:

- Stricter or looser thresholds  
- Environment-specific codes (e.g. “staging factory share is OK until 40%”)  
- Extra checks on top of `core-hints.json` without forking the product pack  

---

## Config

In Admin Console App `appsettings` (or environment overrides):

```json
"AdminConsole": {
  "Hints": {
    "RuleFiles": [ "hints/*.json" ],
    "DisabledCodes": [],
    "DisabledStatePath": "hints/disabled.local.json"
  }
}
```

Production / Docker defaults use the operator data directory instead:

```json
"Hints": {
  "RuleFiles": [ "data/rules/*.json" ],
  "DisabledStatePath": "data/disabled.local.json"
}
```

| Key | Meaning |
|-----|---------|
| **`RuleFiles`** | Paths/globs relative to the Admin content root. `core-hints.json` is **always** loaded, even if omitted here. |
| **`DisabledCodes`** | Codes that never fire (good for shared deploys in source control). |
| **`DisabledStatePath`** | Written by the Settings UI checkboxes. Keep out of git; mount as a volume in Docker so disables survive restart. |

**Load order**

1. `hints/core-hints.json` (required)  
2. Each match of `RuleFiles` (skips `disabled.local.json` and `*.sample.json`; skips duplicates)

**Disable without deleting a rule**

1. UI **Settings** → uncheck the code → `disabled.local.json`  
2. Config `DisabledCodes: [ "some-code" ]`  
3. In JSON: `"enabled": false` on that rule  

The same `code` may exist for both `domain` and `endpoint` scope; disabling the code disables both.

---

## Step-by-step: add a new rule

### 1. Create a pack

Example: `hints/team-ops.json`

```json
{
  "name": "team-ops",
  "rules": [
    {
      "code": "team-high-factory",
      "severity": "Warning",
      "category": "Factory",
      "scope": "domain",
      "description": "Team threshold: factory share above 30% with enough traffic",
      "enabled": true,
      "when": {
        "all": [
          { "path": "domain.requests", "op": ">=", "value": 20 },
          { "path": "domain.fc.factoryShare", "op": ">=", "value": 0.30 }
        ]
      },
      "message": "Factory is {domain.fc.factoryShare:p1} of {domain.requests} requests on {domain.name} — check TTL and key cardinality."
    }
  ]
}
```

### 2. Ensure it is loaded

With `"RuleFiles": [ "hints/*.json" ]`, any non-sample `hints/*.json` is loaded on startup and on **Settings → Reload**.

### 3. Validate in Settings

1. Open Admin UI → **Settings**.  
2. Find group `file:hints/team-ops.json`.  
3. If something is wrong, the red **ERROR** card lists **Rule** + **Path** (path is *inside* the rule, e.g. `when.all[0].op`).  
4. Click a catalog row to open the rule JSON (readable operators like `>=`).

### 4. Verify with traffic

Generate load on the monitored apps, then open **Hints** or a domain detail page. The hint appears only when `when` matches.

### 5. Optional short badge label

List badges use `wwwroot/js/hints.js` → `shortHint()`. Unknown codes still show; add a short map entry if you want a custom abbreviation.

---

## Document shape

```json
{
  "name": "optional-pack-name",
  "rules": [
    {
      "code": "stable-kebab-id",
      "severity": "Warning",
      "category": "Factory",
      "scope": "domain",
      "description": "Shown in Settings catalog",
      "enabled": true,
      "when": { },
      "message": "Human text with {placeholders}"
    }
  ]
}
```

| Field | Required | Notes |
|-------|----------|--------|
| `code` | yes | Stable id: letters, digits, `-`, `_`, `.` (max 80) |
| `severity` | no | `Info` (default), `Warning`, `Critical` |
| `scope` | no | `domain` (default), `endpoint`, or `any` |
| `category` | no | Grouping label in Settings |
| `description` | no | Catalog text (defaults to message) |
| `enabled` | no | Default `true` |
| `when` | yes | Condition tree |
| `message` | yes | Shown on Hints / detail pages |

### Severity meaning

| Level | Use for |
|-------|---------|
| **Critical** | Pipeline badly wrong (e.g. factory share ≥ 50%, factory mostly failing) |
| **Warning** | Fault worth fixing soon |
| **Info** | Expected temporary / operational note |

**Factory share** = `factoryRuns / requests` (API: `factoryShare`; obsolete synonym `originShare`). **Factory** is also known as **origin** in CDN terms. Prefer factory share and factory failure rate over raw Fusion *layer* hit rate. A 0% FC layer rate with low factory share is often normal when Output Cache serves most traffic.

### Conditions

| Form | Meaning |
|------|---------|
| `{ "all": [ … ] }` | Every child true |
| `{ "any": [ … ] }` | At least one true |
| `{ "not": { … } }` | Negate one child |
| `{ "path", "op", "value" }` | Compare |

**Ops:** `eq`, `ne`, `gt`, `gte`, `lt`, `lte`, `exists`, `notexists`, `contains`  
(Aliases: `==`, `!=`, `>`, `>=`, `<`, `<=`.)

### Message placeholders

| Template | Result |
|----------|--------|
| `{domain.name}` | Domain name |
| `{domain.fc.factoryShare:p1}` | e.g. `32.5%` (ratio × 100, 1 decimal); `originShare` still works |
| `{domain.requests}` | Number as text |
| `{path:0.#}` | Numeric format |

### Scope

| Scope | Evaluated for |
|-------|----------------|
| `domain` | Each aggregated domain (+ config when known) |
| `endpoint` | Each aggregated endpoint |
| `any` | Both; guard carefully if you only want one |

Rules are unique by **`(code, scope)`**. Learn by reading **`core-hints.json`** in this folder.

---

## Fact paths (allowlist)

The compiler rejects unknown paths. Common ones:

| Path | Meaning |
|------|---------|
| `domain.requests` | Aggregated request count |
| `domain.fc.factoryShare` | Factory share (also known as origin) = factory runs ÷ requests (0–1) |
| `domain.fc.originShare` | Obsolete synonym for `factoryShare` |
| `domain.fc.staleShare` | Stale share 0–1 |
| `domain.fc.factoryRuns` / `factoryFailures` / `factoryFailureRate` | Factory health |
| `domain.invalidations` / `domain.invalidationShare` | Invalidation pressure |
| `domain.schedulePhase` | e.g. `approaching`, `hold` |
| `domain.versionIsRuntimeOverride` | Runtime Version overlay |
| `domain.instanceSpread.ocHitShare.stdev` | Cross-instance OC drift |
| `endpoint.route` / `endpoint.requests` / `endpoint.fc.*` | Per-route facts |
| `config.outputCacheTtlSeconds` / `clientTtlSeconds` / … | Effective config |
| `config.hasSchedule` | Computed |
| `config.holdAgeHours` | Hours since `ScheduledUpdateUtc` |
| `config.clientTtlCannotRamp` | Client min ≥ max |
| `config.fusionHardLtSoft` | Soft TTL &gt; hard TTL |

Full list: Admin **Settings → Known paths**, or `GET /api/hints/rules` → `knownPaths`.

---

## Settings UI & API

| Action | Where |
|--------|--------|
| List packs / codes | **Settings** (groups per file; click header to collapse) |
| View rule JSON | Click a catalog row |
| Enable/disable | Checkbox (→ `disabled.local.json`) |
| Reload files | **Reload** without process restart |
| Catalog API | `GET /api/hints/rules` |
| Reload API | `POST /api/hints/reload` |
| Toggle API | `PUT /api/hints/rules/{code}/enabled` body `{ "enabled": true }` |

---

## Design checklist

1. Gate noisy rules with min traffic (e.g. `domain.requests >= 20`).  
2. Keep **codes stable** (kebab-case); messages can change.  
3. No side effects — hints never write Version/TTL/invalidate.  
4. Use Info for expected temporary states; Warning/Critical for faults.  
5. Validate in Settings before relying on production dashboards.  
6. Prefer team packs over editing `core-hints.json` (easier upgrades).

---

## Related

- Admin Console App overview: `../README.md`  
- Repository architecture notes: monorepo `docs/admin-hints.md` and `docs/admin.md` (source checkout; may not ship with a standalone Admin publish)
