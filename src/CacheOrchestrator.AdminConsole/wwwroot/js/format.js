/**
 * Display formatting helpers (HTML-safe strings, percentages, units, pipeline bar).
 *
 * Unit convention: thin space (U+2009) between number and unit — e.g. "5 m", "11 ms".
 * Pure counts stay locale-formatted numbers without a unit suffix.
 */

/** Escape text for safe interpolation into HTML templates. */
export function esc(s) {
  return String(s ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

/**
 * Mark config/identity as not time-scoped (white text + green dashed underline, like low-sample).
 * Empty / em dash values are plain (no underline).
 * @param {string} htmlInner Already-escaped or safe HTML fragment for the value
 * @param {string} [tip]
 */
export function currentValueHtml(htmlInner, tip) {
  const plain = String(htmlInner ?? "")
    .replace(/<[^>]*>/g, "")
    .replace(/&mdash;|&#8212;|—|–|-/g, "—")
    .trim();
  if (!plain || plain === "—" || plain === "n/a" || plain === "N/A") {
    return htmlInner || "—";
  }
  const t = tip || "Current value (not scoped to the selected time range)";
  return `<span class="current-value" title="${esc(t)}">${htmlInner}</span>`;
}

/** Em dash for missing window samples — plain text, no underline. */
export function noDataHtml(tip) {
  const t = tip || "No samples in the selected time range";
  return `<span class="no-data-value" title="${esc(t)}">—</span>`;
}

/**
 * Format a 0–1 ratio as a percentage string.
 * @param {number|null|undefined} rate
 * @param {boolean} [lowSample] when true, dashed underline + tooltip
 * @param {"request"|"layer"} [kind] request = total requests &lt; 20; layer = layer hits+misses &lt; 20
 */
export function pct(rate, lowSample, kind = "request") {
  if (rate == null || Number.isNaN(rate)) return "—";
  const s = (rate * 100).toFixed(1) + "%";
  if (!lowSample) return s;
  const tip = kind === "layer"
    ? "Low layer sample (hits+misses on this layer &lt; 20) — rate may be noisy"
    : "Low request sample (total requests &lt; 20) — share may be noisy";
  return `<span class="low-n" title="${tip}">${s}</span>`;
}

/** Locale-aware integer/number formatting; null → em dash. */
export function num(n) {
  if (n == null) return "—";
  return Number(n).toLocaleString();
}

/**
 * Tooltips for Admin metric / product terms (tables, KPIs, chrome, charts).
 * Factory is also known as origin (CDN miss path).
 */
export const METRIC_TITLES = {
  req: "Lifetime request count (sum across selected instances)",
  pipeline:
    "Request pipeline (shares of all requests): OC hit share · FC hit share · Factory share · Bypass · Other. The three main shares are mutually exclusive for a typical OC-then-FC path; Bypass/Other cover the rest.",
  oc: "Output Cache — full HTTP response cache in ASP.NET Core",
  fc: "FusionCache — application object cache (L1 memory, optional L2)",
  // Keep wording aligned with UI labels: "OC hit share", "FC hit share", "Factory share".
  ocHitShare:
    "OC hit share — fraction of all requests served from Output Cache (full HTTP response). Not a layer-only rate.",
  fcHitShare:
    "FC hit share — fraction of all requests served from FusionCache without running the factory.",
  factoryShare:
    "Factory share — fraction of all requests where the Fusion factory ran (factoryRuns ÷ requests). Also known as origin share.",
  factoryAvoidance:
    "Factory avoidance — 1 − factoryRuns/requests (share of requests that did not run the factory). Process lifetime unless a metrics window is used.",
  estTimeSaved:
    "Estimated factory time saved — avoided factory calls × average factory duration (ms). Requires Admin TrackLatency / factory duration samples.",
  cacheBenefit:
    "Cache benefit band (HIGH / MEDIUM / LOW_GAIN / AT_RISK / LOW / UNKNOWN) from avoidance × factory cost.",
  cacheCandidate:
    "Cache candidate / worthiness (STRONG / VOLUME / LIMITED / POOR / NEEDS_TUNING / INSUFFICIENT_DATA) from traffic × cost × factory share.",
  factory: "Fusion factory runs (GetOrSet miss path that produced a value)",
  factoryFailures: "Fusion factory runs that threw or failed",
  factoryFailureRate: "Factory failures ÷ factory runs",
  fcMissRate: "FusionCache miss rate of traffic that reached the Fusion layer (not of all requests)",
  fcMissShare: "FusionCache miss share of all requests",
  fcHitRate: "FusionCache hit rate of traffic that reached the Fusion layer",
  stale: "Fusion fail-safe stale serves (count) — old value returned after factory/timeout issues",
  staleShare: "Stale serves as a share of all requests",
  staleRate: "Stale rate of traffic that reached the Fusion layer",
  inv: "Lifetime domain invalidations (sum)",
  invShare: "Invalidations relative to request volume",
  version: "Domain cache version stamp (key segment). Bump Version to cut over keys without purging by tag.",
  uptime: "Process uptime from last successful health probe",
  latency: "Health probe round-trip latency (Admin → instance)",
  status: "Instance health: Healthy / Degraded / Down",
  hints: "Recommendation hints for this row (severity-colored)",
  route: "Stable endpoint key: HTTP method + route template",
  domain: "Cache domain — named policy group (TTL, providers, client headers, version)",
  instance: "Target app instance id (Cache:InstanceId)",
  url: "Base URL used by Admin fan-out for this instance",
  error: "Last probe or fan-out error message",
  reqRate: "Request rate from external metrics store (windowed)",
  ocHitWindow: "Output Cache hit share over the selected metrics window",
  fcHitRateWindow: "Fusion layer hit rate over the selected metrics window",
  invRate: "Invalidation rate from external metrics store (windowed)",
  schedule: "Client Cache Schedule — ramps client max-age toward a cutover (Calm / Approaching / Hold)",
  softTtl: "Fusion soft TTL — preferred freshness; after this, factory may refresh in background",
  hardTtl: "Fusion hard TTL — absolute max age before entry is not used without factory",
  failSafe: "Fusion fail-safe window — may serve stale if factory fails",
  clientTtl: "Client Cache-Control max-age (browser/CDN), separate from server OC/Fusion TTL",
  bus: "Cluster bus — HTTP fan-out of invalidation / runtime commands between instances",
  fanout: "Admin HTTP fan-out — Admin calls each instance Local Admin API directly",
  overlay: "Runtime Version/TTL overlay — process-local override (not config file)",
  metricsStore: "Optional Prometheus-compatible store for windowed charts (not lifetime counters)",
  l1: "Fusion L1 — in-process memory cache",
  l2: "Fusion L2 — optional distributed cache (e.g. Redis)",
};

/**
 * Prefer factoryShare; fall back to obsolete originShare for older payloads.
 * @param {{ factoryShare?: number|null, originShare?: number|null }|null|undefined} o
 */
export function factoryShareOf(o) {
  if (!o) return null;
  if (o.factoryShare != null) return o.factoryShare;
  if (o.originShare != null) return o.originShare;
  return null;
}

/** @param {{ impact?: { factoryAvoidance?: number|null }|null }|null|undefined} row */
export function factoryAvoidanceOf(row) {
  const v = row?.impact?.factoryAvoidance;
  return v == null ? null : v;
}

/**
 * Human label for benefit/candidate bands.
 * INSUFFICIENT_DATA → "low sample"; UNKNOWN (benefit) uses the same muted/dashed style.
 * @param {string|null|undefined} band
 * @param {{ html?: boolean }} [opts] when html, wrap low-confidence labels for CSS
 */
export function impactBandLabel(band, opts = {}) {
  if (!band) return "—";
  const raw = String(band).toUpperCase();
  if (raw === "INSUFFICIENT_DATA") {
    if (opts.html) {
      return `<span class="low-sample-label" title="Low sample (total requests &lt; 20) — impact bands are not reliable yet">low sample</span>`;
    }
    return "low sample";
  }
  if (raw === "UNKNOWN") {
    if (opts.html) {
      return `<span class="low-sample-label" title="Unknown — not enough traffic sample, or factory cost (duration/size) not measured yet">unknown</span>`;
    }
    return "unknown";
  }
  return String(band).replaceAll("_", " ");
}

/**
 * Human duration for estimated time saved (ms → s/min when large).
 * @param {number|null|undefined} ms
 */
export function fmtDurationMs(ms) {
  if (ms == null || Number.isNaN(ms)) return "—";
  const n = Number(ms);
  if (n < 1000) return `${Math.round(n)} ms`;
  if (n < 60_000) return `${(n / 1000).toFixed(1)} s`;
  if (n < 3_600_000) return `${(n / 60_000).toFixed(1)} min`;
  return `${(n / 3_600_000).toFixed(2)} h`;
}

/** HTML title attribute from METRIC_TITLES key or raw text. */
export function tipAttr(keyOrText) {
  const tip = METRIC_TITLES[keyOrText] || keyOrText || "";
  return tip ? ` title="${esc(tip)}"` : "";
}

/**
 * Table header cell with metric tooltip.
 * @param {string} label Visible header text
 * @param {string} title Tooltip (or METRIC_TITLES key if `fromKey`)
 * @param {{ className?: string, fromKey?: boolean }} [opts]
 */
export function thMetric(label, title, opts = {}) {
  const tip = opts.fromKey ? (METRIC_TITLES[title] || title) : title;
  const cls = opts.className ? ` class="${esc(opts.className)}"` : "";
  return `<th${cls} title="${esc(tip)}">${esc(label)}</th>`;
}

/**
 * Horizontal request-pipeline share bar (OC hit share · FC hit share · Factory share · Bypass · Other).
 * @param {object|null} p Pipeline DTO with *Share fields
 * @param {boolean} [large] Wider/taller bar for detail pages
 */
export function pipelineBar(p, large) {
  if (!p) return `<div class="pipe empty"></div>`;
  const factoryShare = factoryShareOf(p);
  const parts = [
    ["oc", p.ocHitShare, "OC hit share"],
    ["fc", p.fcHitShare, "FC hit share"],
    ["origin", factoryShare, "Factory share"],
    ["bypass", p.bypassShare, "Bypass"],
    ["other", p.otherShare, "Other"],
  ].filter(([, v]) => v != null && v > 0.0005);
  if (!parts.length) return `<div class="pipe empty${large ? " lg" : ""}"></div>`;
  return `<div class="pipe${large ? " lg" : ""}" title="${esc(METRIC_TITLES.pipeline)}">${
    parts.map(([cls, v, label]) =>
      `<span class="seg ${cls}" style="flex:${Math.max(v, 0.01)}" title="${label}: ${(v * 100).toFixed(1)}%"></span>`
    ).join("")
  }</div>`;
}

/** Min–max–mean share cell when an entity is split across instances. */
export function spreadCell(s) {
  if (!s || s.sampleCount < 1) return "—";
  if (s.sampleCount === 1) return pct(s.mean);
  return `${pct(s.min)}–${pct(s.max)} <span class="muted">μ ${pct(s.mean)}</span>`;
}

/**
 * Number + unit with thin space. Accepts raw numbers (locale-formatted)
 * or an already-safe display string.
 */
export function fmtUnit(value, unit) {
  if (value == null || value === "") return "—";
  if (typeof value === "number") {
    if (Number.isNaN(value)) return "—";
    return `${num(value)}\u2009${unit}`;
  }
  const s = String(value).trim();
  if (!s || s === "—") return "—";
  return `${s}\u2009${unit}`;
}

/** Human uptime from whole seconds: "3 h 12 m", "45 s", etc. */
export function formatUptime(seconds) {
  if (seconds == null || seconds < 0 || Number.isNaN(Number(seconds))) return "—";
  const s = Math.floor(Number(seconds));
  const d = Math.floor(s / 86400);
  const h = Math.floor((s % 86400) / 3600);
  const m = Math.floor((s % 3600) / 60);
  const sec = s % 60;
  if (d > 0) return `${fmtUnit(d, "d")} ${fmtUnit(h, "h")}`;
  if (h > 0) return `${fmtUnit(h, "h")} ${fmtUnit(m, "m")}`;
  if (m > 0) return `${fmtUnit(m, "m")} ${fmtUnit(sec, "s")}`;
  return fmtUnit(sec, "s");
}

/** Round-trip latency for health probes. */
export function formatLatencyMs(ms) {
  if (ms == null || Number.isNaN(Number(ms))) return "—";
  return fmtUnit(Math.round(Number(ms)), "ms");
}

/** Compact duration labels for chrome (refresh interval): "5 s", "1 m". */
export function formatDurationLabel(seconds) {
  if (seconds == null || Number.isNaN(Number(seconds))) return "—";
  const s = Number(seconds);
  if (s >= 60 && s % 60 === 0) return fmtUnit(s / 60, "m");
  return fmtUnit(s, "s");
}
