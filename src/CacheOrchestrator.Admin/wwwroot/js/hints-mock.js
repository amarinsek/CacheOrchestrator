/**
 * =============================================================================
 * REMOVE LATER — Hint mockup (UI preview only)
 * =============================================================================
 * Toggle on the Hints page; persists in localStorage. When enabled, injects
 * fake recommendation hints on Overview / lists / detail so designers can
 * review symbology before real traffic produces dense live rules.
 *
 * Delete this module and every import of applyMock* / isHintMock / setHintMock
 * once live recommendation density is enough for design review.
 *
 * Search codebase: "REMOVE LATER — Hint mockup"
 * =============================================================================
 */

// Local summary (avoid circular import with hints.js which imports this module).
function summarizeHintsLocal(hints) {
  let info = 0;
  let warning = 0;
  let critical = 0;
  for (const h of hints || []) {
    if (h.severity === "Critical") critical++;
    else if (h.severity === "Warning") warning++;
    else info++;
  }
  return {
    info,
    warning,
    critical,
    total: info + warning + critical,
    maxSeverity: critical ? "Critical" : warning ? "Warning" : info ? "Info" : "None",
  };
}

const HINT_MOCK_KEY = "adminHintMock";

/** Whether mock hints are enabled (localStorage). */
export function isHintMock() {
  return localStorage.getItem(HINT_MOCK_KEY) === "1";
}

/** Persist mock toggle. */
export function setHintMock(on) {
  localStorage.setItem(HINT_MOCK_KEY, on ? "1" : "0");
}

/**
 * Catalog of sample recommendation objects (mirrors server-side codes where possible).
 * @type {ReadonlyArray<{severity: string, code: string, message: string}>}
 */
export const MOCK_HINT_CATALOG = Object.freeze([
  {
    severity: "Critical",
    code: "high-origin-share",
    message:
      "Origin/factory share is high — short TTL or frequent misses; consider soft/hard TTL and eager refresh.",
  },
  {
    severity: "Warning",
    code: "low-fc-hit-rate",
    message:
      "FC layer hit rate below 60% with enough traffic — consider longer Fusion/Output TTL.",
  },
  {
    severity: "Warning",
    code: "elevated-stale",
    message: "Stale serves are elevated — factory failures or fail-safe in use.",
  },
  {
    severity: "Warning",
    code: "instance-oc-hit-spread",
    message:
      "OC hit share varies across instances — check L1 consistency / uneven traffic.",
  },
  {
    severity: "Info",
    code: "client-ttl-gt-output",
    message:
      "Client TTL ≫ Output TTL — align the ratio to avoid stale browser cache.",
  },
  {
    severity: "Info",
    code: "schedule-phase",
    message:
      "Client Cache Schedule is approaching/hold — verify ScheduledUpdateUtc.",
  },
  {
    severity: "Info",
    code: "fc-miss-rate-vs-oc-share",
    message:
      "FC miss rate looks high only on rare OC misses — prefer request shares.",
  },
]);

/** Deterministic pseudo-random pick of 1–3 catalog hints from a seed. */
export function mockHintsFor(seed) {
  const n = (seed || 0) % MOCK_HINT_CATALOG.length;
  const count = 1 + (seed % 3);
  const out = [];
  for (let k = 0; k < count; k++) {
    out.push(MOCK_HINT_CATALOG[(n + k) % MOCK_HINT_CATALOG.length]);
  }
  return out;
}

/** Simple string hash → non-negative int (stable seeds for routes/ids). */
export function hashSeed(s) {
  let h = 0;
  const str = String(s || "");
  for (let i = 0; i < str.length; i++) h = ((h << 5) - h + str.charCodeAt(i)) | 0;
  return Math.abs(h);
}

/** Attach mock hints to an endpoint DTO when mock mode is on. */
export function applyMockToEndpoint(e, i = 0) {
  if (!isHintMock()) return e;
  const hints = mockHintsFor(hashSeed(e.route) + i);
  return { ...e, hints };
}

/** Attach mock hints to a domain DTO (and nested endpoints). */
export function applyMockToDomain(d, i = 0) {
  if (!isHintMock()) return d;
  const hints = mockHintsFor(hashSeed(d.name) + i + 1);
  return {
    ...d,
    hints,
    endpoints: (d.endpoints || []).map((e, j) => applyMockToEndpoint(e, j)),
  };
}

/** Attach mock hintSummary to an instance status DTO. */
export function applyMockToInstance(inst, i = 0) {
  if (!isHintMock()) return inst;
  const hints = mockHintsFor(hashSeed(inst.id) + i + 2);
  return { ...inst, hintSummary: summarizeHintsLocal(hints), _mockHints: hints };
}

/** Rewrite overview payload with mock hints for every surface. */
export function applyMockToOverview(o) {
  if (!isHintMock()) return o;
  const topEp = (o.topEndpoints || []).map((e, i) => applyMockToEndpoint(e, i));
  const topDom = (o.topDomains || []).map((d, i) => applyMockToDomain(d, i));
  const instances = (o.instances || []).map((inst, i) => applyMockToInstance(inst, i));
  const all = [];
  topEp.forEach((e) => all.push(...(e.hints || [])));
  topDom.forEach((d) => all.push(...(d.hints || [])));
  instances.forEach((inst) => all.push(...(inst._mockHints || [])));
  // Pad with full catalog so header / Hints page have demo density.
  all.push(...MOCK_HINT_CATALOG);
  return {
    ...o,
    topEndpoints: topEp,
    topDomains: topDom,
    instances,
    hintSummary: summarizeHintsLocal(all),
    topHints: MOCK_HINT_CATALOG,
  };
}
