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
 * Convention: always <c>Label - description</c> (em dash not used as separator).
 * Factory is also known as origin (CDN miss path).
 */
export const METRIC_TITLES = {
  req: "Req - Number of cache-accounted requests in the selected time range.",
  pipeline:
    "Pipeline - Exclusive mix of requests: OC hit, DC hit (fresh), or FA run (factory invoked). DC stale is an overlay, not a mix segment.",
  oc: "OC - Output Cache (full HTTP response cache).",
  dc: "DC - data cache (application data cache; in-process memory, optional distributed L2).",
  outputCacheHitShare:
    "OC hit % - Share of requests served from Output Cache (full HTTP response) in the selected time range.",
  dataCacheHitShare:
    "DC hit % - Share of requests served from data cache without running the factory in the selected time range.",
  factoryShare:
    "FA run % - Share of requests where the value factory ran (including data cache disabled, unresolved, bypass, miss, and stale).",
  factoryAvoidance:
    "Factory avoidance - Share of requests that did not run the factory in the selected time range.",
  estTimeSaved:
    "EFTS - Estimated factory time saved in the selected time range (avoided factory calls × average factory duration). Cluster total sums domains.",
  reqRate: "PRPS - Peak requests per second: highest 1-minute request rate in the selected time range.",
  peakRequestRate: "PRPS - Peak requests per second: highest 1-minute request rate in the selected time range.",
  liveRps: "RPS - Current request rate over the last minute.",
  rpsWindow: "RPS - Requests per second in the selected time range.",
  cacheBenefit:
    "Benefit - How beneficial caching looks for this traffic (avoidance × factory cost) in the selected time range.",
  cacheCandidate:
    "Candidate - Whether this traffic looks like a strong caching candidate in the selected time range.",
  factory: "Factory runs - Times the value factory ran in the selected time range.",
  factoryFailures:
    "FAFC - Factory failure count in the selected time range (hard factory throw and/or fail-safe after factory issues).",
  factoryFailureRate: "FA failure rate - Factory failures divided by factory runs in the selected time range.",
  dcMissRate: "DC miss rate - Miss rate among requests that reached data cache (layer rate).",
  dcMissShare: "DC miss % - data cache misses as a share of all requests in the selected time range.",
  dataCacheHitRate: "DC hit rate - Hit rate among requests that reached data cache (layer rate).",
  stale: "Stale - Fail-safe stale serves (an older value was returned after factory or timeout issues).",
  staleShare: "DC stale % - Fail-safe stale serves as a share of all requests (overlay; also counted in FA run %).",
  staleRate: "Stale rate - Stale rate among traffic that reached data cache (layer rate).",
  inv: "Inv - Successful invalidations in the selected time range.",
  invShare: "Inv share - Invalidations relative to request volume in the selected time range.",
  version: "Version - Domain version stamp used in cache keys. Change it to cut over to new keys.",
  uptime: "Uptime - How long this app process has been running.",
  latency: "Latency - Round-trip time of the last health check to this instance.",
  status: "Status - Instance health: Healthy, Degraded, or Down.",
  hints: "Hints - Recommendations for this row.",
  route: "Route - Endpoint key: HTTP method + route template.",
  domain: "Domain - Cache domain (shared TTLs, providers, client headers, and version).",
  instance: "Instance - Instance id (Cache:InstanceId).",
  url: "URL - Base URL used to reach this instance.",
  error: "Error - Last health-check or connection error.",
  outputCacheHitWindow: "OC hit % - Output Cache hit share in the selected time range.",
  dataCacheHitRateWindow: "DC hit rate - Data-cache hit rate in the selected time range.",
  invRate: "Inv / s - Invalidation rate in the selected time range.",
  schedule: "Schedule - Client Cache Schedule (adjusts browser/CDN max-age toward a planned cutover).",
  outputTtl: "Output TTL - Output Cache TTL for this domain.",
  softTtl: "Data soft TTL - Preferred freshness window for the data cache.",
  hardTtl: "Data hard TTL - Absolute maximum age for the data cache (when the provider supports it).",
  failSafe: "Fail-safe - Window that may serve stale data if the factory fails (provider-dependent).",
  clientTtl: "Client TTL / min - Client Cache-Control max-age (browser/CDN), separate from server TTLs.",
  schedulePhase: "Schedule phase - Client Cache Schedule phase currently applied for this domain.",
  dcInstance: "DC instance - data cache named instance used by this domain.",
  bus: "Bus - Cluster bus (instances apply commands to peers over HTTP).",
  fanout: "Fan-out - Admin calls each instance directly to apply the operation.",
  overlay: "Overlay - Runtime Version/TTL override on this process (not from config file).",
  metricsStore: "Metrics - Metrics backend used for Live, tables, and charts.",
  l1: "L1 - Data-cache L1 (in-process memory).",
  l2: "L2 - Data-cache L2 (optional distributed store, for example Redis).",
  entities: "Traffic entities - Domains and endpoints with traffic in the selected time range.",
  avgFactoryDuration: "FAD - Average factory duration (ms) in the selected time range.",
  timeSavedRatio: "Time-saved ratio - EFTS / (EFTS + factory duration paid).",
  avgResultSize: "Avg result size - Average measured factory result size.",
  payloadOffload: "Est. payload offload - Avoided factory calls × avg result size.",
  durationSamples: "Duration samples - Factory duration samples (0 if TrackLatency is off).",
  sizeSamples: "Size samples - Factory result size samples (0 if TrackResultSize is off).",
  outputCacheHits: "Hits - Output Cache hits in the selected time range.",
  outputCacheMisses: "Misses - Output Cache misses in the selected time range.",
  outputCacheBypass: "Bypass - Output Cache skipped this request (auth / no-store).",
  outputCacheOff: "Off - Output Cache disabled for the domain.",
  outputCacheOffShare: "OC off % - Share of requests while Output Cache was disabled.",
  ocLayerN: "Layer n - Samples that reached the Output Cache layer.",
  ocMissShare: "OC miss % - Output Cache miss share of all requests.",
  outputCacheBypassShare: "OC bypass % - Auth / no-store skip share of all requests.",
  outputCacheHitRate: "OC hit rate - Hit rate of traffic that reached Output Cache (layer rate).",
  ocMissRate: "OC miss rate - Miss rate of traffic that reached Output Cache (layer rate).",
  dataCacheHits: "Hits - data cache hits in the selected time range.",
  dataCacheMisses: "Misses - data cache misses in the selected time range.",
  dataCacheBypass: "Bypass - data cache bypass in the selected time range.",
  dcLayerN: "Layer n - Samples that reached the data-cache layer.",
  factoryRate: "Factory / s - Factory callback rate over the lookback window (including data cache disabled).",
  factoryFailShare: "Fail % - Data-cache fail + stale share of requests over the lookback window.",
  bypassShare: "Bypass % - Auth / no-store skip at a cache layer (not an exclusive pipeline mix bucket).",
};

/**
 * FAFC cell: factory failure count. Orange when &gt; 0; red when failures are
 * a large share of factory runs (≥ 10%). No title — table tooltips live on headers only.
 * @param {{ factoryFailures?: number|null, factoryRuns?: number|null }|null|undefined} fc
 * @param {{ tag?: "td"|"span" }} [opts]
 */
export function fafcHtml(fc, opts = {}) {
  const tag = opts.tag || "td";
  const n = Number(fc?.factoryFailures ?? 0);
  const runs = Number(fc?.factoryRuns ?? 0);
  const rate = runs > 0 ? n / runs : null;
  let cls = "col-num col-fafc";
  if (n > 0) {
    cls += rate != null && rate >= 0.1 ? " metric-bad" : " metric-warn";
  }
  return `<${tag} class="${cls}">${num(n)}</${tag}>`;
}

/**
 * DC stale % cell (request share of fail-safe stale serves).
 * No title — table tooltips live on headers only.
 * @param {{ staleShare?: number|null, lowRequestSample?: boolean }|null|undefined} fc
 * @param {{ tag?: "td"|"span" }} [opts]
 */
export function staleShareHtml(fc, opts = {}) {
  const tag = opts.tag || "td";
  const share = fc?.staleShare;
  const body = pct(share, fc?.lowRequestSample, "request");
  let cls = "col-metric col-stale";
  if (share != null && share >= 0.1) cls += " metric-bad";
  else if (share != null && share > 0) cls += " metric-warn";
  return `<${tag} class="${cls}">${body}</${tag}>`;
}

/**
 * @param {{ factoryShare?: number|null }|null|undefined} o
 */
export function factoryShareOf(o) {
  if (!o) return null;
  return o.factoryShare != null ? o.factoryShare : null;
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
  let tip = opts.fromKey ? (METRIC_TITLES[title] || title) : title;
  // Ensure "Label - description" even if a raw description slipped through.
  if (tip && label && !String(tip).includes(" - ")) {
    tip = `${label} - ${tip}`;
  }
  const cls = opts.className ? ` class="${esc(opts.className)}"` : "";
  return `<th${cls} title="${esc(tip)}">${esc(label)}</th>`;
}

/**
 * Horizontal request-pipeline share bar (exclusive mix: OC hit · DC hit · FA run).
 * @param {object|null} p Pipeline DTO with *Share fields
 * @param {boolean} [large] Wider/taller bar for detail pages
 * @param {{ title?: boolean, segmentTips?: boolean }} [opts]
 *   title: wrap tooltip (default true). segmentTips: per-segment value tips (default true).
 *   In entity tables pass <c>{ title: false, segmentTips: false }</c> — column header carries the tip.
 */
export function pipelineBar(p, large, opts = {}) {
  if (!p) return `<div class="pipe empty"></div>`;
  const showTitle = opts.title !== false;
  const showSegTips = opts.segmentTips !== false;
  const factoryShare = factoryShareOf(p);
  const parts = [
    ["oc", p.outputCacheHitShare, "OC hit"],
    ["dc", p.dataCacheHitShare, "DC hit"],
    ["origin", factoryShare, "FA run"],
  ].filter(([, v]) => v != null && v > 0.0005);
  if (!parts.length) return `<div class="pipe empty${large ? " lg" : ""}"></div>`;
  const wrapTip = showTitle ? ` title="${esc(METRIC_TITLES.pipeline)}"` : "";
  return `<div class="pipe${large ? " lg" : ""}"${wrapTip}>${
    parts.map(([cls, v, label]) => {
      const segTip = showSegTips
        ? ` title="${esc(`${label} - ${(v * 100).toFixed(1)}%`)}"`
        : "";
      return `<span class="seg ${cls}" style="flex:${Math.max(v, 0.01)}"${segTip}></span>`;
    }).join("")
  }</div>`;
}

/**
 * Pipeline panel: exclusive mix only (OC hit · DC hit · FA run).
 * No separate heading — "Pipeline" is the first column header.
 * @param {object|null|undefined} p Pipeline DTO
 */
export function pipelinePanelHtml(p) {
  const bar = pipelineBar(p, true, { title: true, segmentTips: true });
  return `
    <div class="pipeline-panel">
      <table class="pipeline-share-table">
        <thead>
          <tr>
            ${thMetric("Pipeline", "pipeline", { fromKey: true, className: "col-pipe" })}
            ${thMetric("OC hit %", "outputCacheHitShare", { fromKey: true, className: "col-num" })}
            ${thMetric("DC hit %", "dataCacheHitShare", { fromKey: true, className: "col-num" })}
            ${thMetric("FA run %", "factoryShare", { fromKey: true, className: "col-num" })}
          </tr>
        </thead>
        <tbody>
          <tr>
            <td class="col-pipe">${bar}</td>
            <td class="col-num">${pct(p?.outputCacheHitShare)}</td>
            <td class="col-num">${pct(p?.dataCacheHitShare)}</td>
            <td class="col-num">${pct(factoryShareOf(p))}</td>
          </tr>
        </tbody>
      </table>
    </div>`;
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
