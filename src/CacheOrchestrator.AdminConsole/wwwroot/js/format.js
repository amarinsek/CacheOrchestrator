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
  const t = tip || "Current value (not part of the selected time range)";
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
  req: "Number of cache-accounted requests in the selected time range.",
  pipeline:
    "How requests were served in the selected time range: Output Cache hit, Fusion hit, factory (origin), bypass, or other.",
  oc: "Output Cache — full HTTP response cache.",
  fc: "FusionCache — application data cache (in-process memory, optional distributed L2).",
  ocHitShare:
    "Share of requests served from Output Cache (full HTTP response) in the selected time range.",
  fcHitShare:
    "Share of requests served from FusionCache without running the factory in the selected time range.",
  factoryShare:
    "Share of requests where the value factory ran (origin / miss path) in the selected time range.",
  factoryAvoidance:
    "Share of requests that did not run the factory in the selected time range.",
  estTimeSaved:
    "Estimated factory time saved in the selected time range (avoided factory calls × average factory duration). Cluster total sums domains.",
  reqRate: "Highest 1-minute request rate in the selected time range.",
  peakRequestRate: "Highest 1-minute request rate in the selected time range.",
  liveRps: "Current request rate over the last minute.",
  cacheBenefit:
    "How beneficial caching looks for this traffic (avoidance × factory cost) in the selected time range.",
  cacheCandidate:
    "Whether this traffic looks like a strong caching candidate in the selected time range.",
  factory: "Times the value factory ran in the selected time range.",
  factoryFailures: "Factory failures (hard error or fail-safe stale) in the selected time range.",
  factoryFailureRate: "Factory failures divided by factory runs in the selected time range.",
  fcMissRate: "Miss rate among requests that reached FusionCache (layer rate).",
  fcMissShare: "FusionCache misses as a share of all requests in the selected time range.",
  fcHitRate: "Hit rate among requests that reached FusionCache (layer rate).",
  stale: "Fail-safe stale serves — an older value was returned after factory or timeout issues.",
  staleShare: "Stale serves as a share of all requests in the selected time range.",
  staleRate: "Stale rate among traffic that reached FusionCache.",
  inv: "Successful invalidations in the selected time range.",
  invShare: "Invalidations relative to request volume in the selected time range.",
  version: "Domain version stamp used in cache keys. Change it to cut over to new keys.",
  uptime: "How long this app process has been running.",
  latency: "Round-trip time of the last health check to this instance.",
  status: "Instance health: Healthy, Degraded, or Down.",
  hints: "Recommendations for this row.",
  route: "Endpoint key: HTTP method + route template.",
  domain: "Cache domain — shared TTLs, providers, client headers, and version.",
  instance: "Instance id (Cache:InstanceId).",
  url: "Base URL used to reach this instance.",
  error: "Last health-check or connection error.",
  ocHitWindow: "Output Cache hit share in the selected time range.",
  fcHitRateWindow: "Fusion hit rate in the selected time range.",
  invRate: "Invalidation rate in the selected time range.",
  schedule: "Client Cache Schedule — adjusts browser/CDN max-age toward a planned cutover.",
  softTtl: "Fusion soft TTL — preferred freshness window.",
  hardTtl: "Fusion hard TTL — absolute maximum age.",
  failSafe: "Fail-safe window — may serve stale data if the factory fails.",
  clientTtl: "Client Cache-Control max-age (browser/CDN), separate from server TTLs.",
  bus: "Cluster bus — instances apply commands to peers over HTTP.",
  fanout: "Admin calls each instance directly to apply the operation.",
  overlay: "Runtime Version/TTL override on this process (not from config file).",
  metricsStore: "Metrics backend used for Live, tables, and charts.",
  l1: "Fusion L1 — in-process memory cache.",
  l2: "Fusion L2 — optional distributed cache (for example Redis).",
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

/**
 * Human uptime from whole seconds.
 * Always two units (except bare seconds) with zero-padded secondary so table cells
 * keep a stable width across soft refresh.
 */
export function formatUptime(seconds) {
  if (seconds == null || seconds < 0 || Number.isNaN(Number(seconds))) return "—";
  const s = Math.floor(Number(seconds));
  const d = Math.floor(s / 86400);
  const h = Math.floor((s % 86400) / 3600);
  const m = Math.floor((s % 3600) / 60);
  const sec = s % 60;
  const p2 = (n) => String(n).padStart(2, "0");
  if (d > 0) return `${d}\u2009d ${p2(h)}\u2009h`;
  if (h > 0) return `${h}\u2009h ${p2(m)}\u2009m`;
  if (m > 0) return `${m}\u2009m ${p2(sec)}\u2009s`;
  return `${sec}\u2009s`;
}

/**
 * Format a request rate (req/s) from Prometheus rate() / max_over_time(rate()).
 * @param {number|null|undefined} rate
 */
export function fmtRequestRate(rate) {
  const r = Number(rate);
  if (!Number.isFinite(r) || r < 0) return "—";
  if (r < 0.01) return r.toFixed(3);
  if (r < 10) return r.toFixed(2);
  if (r < 100) return r.toFixed(1);
  return num(Math.round(r));
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
