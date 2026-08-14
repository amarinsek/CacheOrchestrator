/**
 * Metrics page — time series from optional external Prometheus-compatible storage.
 */

import { api } from "./api.js";
import { lineChartHtml } from "./charts.js";
import { $, main, mainHasContent, paintMain } from "./dom.js";
import { esc, num, pct } from "./format.js";
import { navigate, setBreadcrumb, setNavActive } from "./router.js";
import { bindEmptyStateActions, emptyStateHtml } from "./tables.js";

const RANGE_OPTS = ["15m", "1h", "6h", "24h", "7d"];

/**
 * @param {URLSearchParams} params
 * @param {{ soft?: boolean }} [opts]
 */
export async function renderMetrics(params = new URLSearchParams(), opts = {}) {
  const soft = !!opts.soft;
  setNavActive("metrics");
  setBreadcrumb([{ label: "Metrics" }]);
  if (!soft || !mainHasContent()) {
    main().innerHTML = `<div class="card"><p class="muted">Loading metrics…</p></div>`;
  }

  let status;
  try {
    status = await api("/api/metrics/status");
  } catch (err) {
    if (soft && mainHasContent()) return;
    paint(`<div class="card">${emptyStateHtml("error", {
      title: "Cannot load metrics status",
      detail: err.message,
    })}</div>`, soft);
    bindEmptyStateActions(main());
    return;
  }

  if (status.status === "NotConfigured") {
    paint(`
      ${metricsStatusBanner(status)}
      <div class="card">${emptyStateHtml("metrics-config", {
        title: "Metrics storage not configured",
        detail: status.error
          || "Set CacheAdmin:Metrics:Enabled, Provider, and BaseUrl to show time series from Prometheus.",
        actions: [
          { label: "Refresh", onclick: "window.__adminRefresh && window.__adminRefresh()" },
          { label: "Overview", href: "#/overview" },
        ],
      })}</div>`, soft);
    bindEmptyStateActions(main());
    return;
  }

  if (status.status === "Disconnected") {
    paint(`
      ${metricsStatusBanner(status)}
      <div class="card">${emptyStateHtml("metrics-offline", {
        title: "Metrics storage not connected",
        detail: status.error || "Probe failed. Check BaseUrl, network, and credentials.",
        actions: [
          { label: "Refresh", onclick: "window.__adminRefresh && window.__adminRefresh()" },
        ],
      })}</div>`, soft);
    bindEmptyStateActions(main());
    return;
  }

  const range = params.get("range") || status.defaultRange || "1h";
  const domains = params.get("domains") || "";

  let summary = null;
  let series = null;
  try {
    const q = new URLSearchParams({ range });
    if (domains) q.set("domains", domains);
    [summary, series] = await Promise.all([
      api("/api/metrics/summary?" + q.toString()),
      api("/api/metrics/series?" + q.toString()),
    ]);
  } catch (err) {
    if (soft && mainHasContent()) return;
    paint(`
      ${metricsStatusBanner(status)}
      <div class="card">${emptyStateHtml("error", { detail: err.message })}</div>`, soft);
    bindEmptyStateActions(main());
    return;
  }

  if (series.status === "Disconnected") {
    paint(`
      ${metricsStatusBanner({ ...status, status: "Disconnected", error: series.error })}
      <div class="card">${emptyStateHtml("metrics-offline", {
        detail: series.error || "Query failed.",
      })}</div>`, soft);
    bindEmptyStateActions(main());
    return;
  }

  // Soft path: only swap live KPI + chart regions when layout already matches.
  if (soft && $("#metricsLiveRoot") && $("#metricsRange")?.value === range) {
    const banner = $("#metricsBannerHost");
    if (banner) banner.innerHTML = metricsStatusBanner(status);
    const kpis = $("#metricsKpis");
    if (kpis) kpis.innerHTML = metricsKpiHtml(summary, series);
    const grid = $("#metricsGrid");
    if (grid) grid.innerHTML = metricsPanelsHtml(series);
    bindEmptyStateActions(main());
    return;
  }

  const rangeSelect = RANGE_OPTS.map((r) =>
    `<option value="${r}"${r === range ? " selected" : ""}>${r}</option>`).join("");

  paint(`
    <div id="metricsBannerHost">${metricsStatusBanner(status)}</div>
    <form class="toolbar" id="metricsToolbar">
      <label>Range
        <select name="range" id="metricsRange">${rangeSelect}</select>
      </label>
      <label>Domains
        <input type="text" name="domains" id="metricsDomains" placeholder="all (comma-separated)"
          value="${esc(domains)}" />
      </label>
      <div class="toolbar-apply">
        <button type="submit">Apply</button>
      </div>
    </form>
    <div id="metricsLiveRoot">
      <div class="kpi-row" id="metricsKpis">${metricsKpiHtml(summary, series)}</div>
      <p class="muted small metrics-note">
        Window metrics from external storage (not lifetime Admin counters).
        Scraped meter: <code>CacheOrchestrator</code>.
      </p>
      <div class="grid-2 metrics-grid" id="metricsGrid">
        ${metricsPanelsHtml(series)}
      </div>
    </div>`, soft);

  bindEmptyStateActions(main());
  $("#metricsToolbar")?.addEventListener("submit", (ev) => {
    ev.preventDefault();
    navigate("metrics", {
      range: $("#metricsRange")?.value || "1h",
      domains: ($("#metricsDomains")?.value || "").trim(),
    });
  });
}

function paint(html, soft) {
  if (soft) paintMain(html);
  else main().innerHTML = html;
}

function metricsKpiHtml(summary, series) {
  return `
      <div class="kpi"><div class="label">Req / s</div><div class="value">${fmtRate(summary?.requestRate)}</div></div>
      <div class="kpi"><div class="label">OC hit (window)</div><div class="value">${fmtShare(summary?.ocHitShare)}</div></div>
      <div class="kpi"><div class="label">FC hit rate</div><div class="value">${fmtShare(summary?.fcHitRate)}</div></div>
      <div class="kpi"><div class="label">Inv / s</div><div class="value">${fmtRate(summary?.invalidationRate)}</div></div>
      <div class="kpi"><div class="label">Step</div><div class="value" style="font-size:1rem">${esc(series?.step || "—")}</div></div>`;
}

function metricsPanelsHtml(series) {
  const panelCards = (series.panels || []).map((p) => {
    const badge = `<span class="badge">${esc(series.range)}</span>`;
    const warn = p.warning
      ? `<p class="muted chart-warn">${esc(p.warning)}</p>`
      : "";
    return `
      <div class="card chart-card" data-panel="${esc(p.id)}">
        <div class="card-head">
          <h2>${esc(p.title)} ${badge}</h2>
          <span class="muted small">${esc(unitLabel(p.unit))}</span>
        </div>
        ${lineChartHtml(p.series || [], { unit: p.unit })}
        ${warn}
      </div>`;
  }).join("");
  return panelCards || `<div class="card">${emptyStateHtml("metrics-empty")}</div>`;
}

/**
 * Status banner shared with Overview embed.
 * @param {{ status: string, provider?: string, host?: string, latencyMs?: number, error?: string }} status
 */
export function metricsStatusBanner(status) {
  if (!status) return "";
  if (status.status === "Connected") {
    const lat = status.latencyMs != null ? ` · probe ${Math.round(status.latencyMs)} ms` : "";
    return `<div class="banner metrics-banner ok">
      <span><span class="badge ok">Connected</span>
        ${esc(status.provider || "Prometheus")}${status.host ? ` · <code>${esc(status.host)}</code>` : ""}${lat}
      </span>
      <span class="banner-actions"><button type="button" class="secondary" data-es-refresh>Refresh</button></span>
    </div>`;
  }
  if (status.status === "Disconnected") {
    return `<div class="banner err metrics-banner">
      <span><span class="badge warn">Disconnected</span>
        Metrics storage not connected${status.error ? ` — ${esc(status.error)}` : ""}
      </span>
      <span class="banner-actions"><button type="button" class="secondary" data-es-refresh>Retry</button></span>
    </div>`;
  }
  if (status.status === "NotConfigured") {
    return `<div class="banner warn metrics-banner">
      <span><span class="badge muted">Not configured</span>
        Time series require <code>CacheAdmin:Metrics</code> (Enabled, Provider, BaseUrl).
      </span>
    </div>`;
  }
  return "";
}

/**
 * Load time-series charts into a detail-page mount.
 * @param {string} mountId element id
 * @param {{ scope: "domain"|"instance"|"endpoint", domain?: string, instanceId?: string, route?: string, range?: string }} opts
 */
export async function mountDetailMetrics(mountId, opts) {
  const el = document.getElementById(mountId);
  if (!el) return;

  const range = opts.range || "1h";
  let status;
  try {
    status = await api("/api/metrics/status");
  } catch {
    el.innerHTML = "";
    return;
  }

  if (status.status === "NotConfigured") {
    el.innerHTML = "";
    return;
  }
  if (status.status === "Disconnected") {
    el.innerHTML = `<div class="card"><h2>Metrics <span class="badge">last ${esc(range)}</span></h2>
      <p class="muted">Metrics storage not connected${status.error ? `: ${esc(status.error)}` : "."}</p>
      <p><a href="#/metrics">Open Metrics →</a></p></div>`;
    return;
  }

  const panels = opts.scope === "domain"
    ? "request_rate,oc_hit_share,fc_hit_rate,invalidation_rate,schedule_phase,fc_p95_ms"
    : opts.scope === "instance"
      ? "request_rate,oc_hit_share,fc_hit_rate,invalidation_rate,fc_p95_ms,cluster_publish_failures"
      : "request_rate,oc_hit_share,fc_hit_rate,fc_p95_ms";

  const q = new URLSearchParams({ range, panels });
  if (opts.domain) q.set("domains", opts.domain);
  if (opts.instanceId) q.set("instances", opts.instanceId);
  if (opts.route) q.set("routes", opts.route);

  let series;
  try {
    series = await api("/api/metrics/series?" + q.toString());
  } catch (err) {
    el.innerHTML = `<div class="card"><h2>Metrics</h2><p class="muted">${esc(err.message)}</p></div>`;
    return;
  }

  if (series.status !== "Connected") {
    el.innerHTML = `<div class="card"><h2>Metrics</h2><p class="muted">${esc(series.error || "Not connected")}</p></div>`;
    return;
  }

  const list = series.panels || [];
  const anyPoints = list.some((p) => (p.series || []).some((s) => (s.points || []).length > 0));
  const title =
    opts.scope === "domain" ? `Domain metrics`
      : opts.scope === "instance" ? `Instance metrics`
        : `Endpoint metrics`;

  if (!anyPoints) {
    const note = opts.scope === "endpoint"
      ? "No samples for this route in the selected range. Possible causes: no traffic, " +
        "<code>Cache:Metrics:IncludeEndpointLabel</code> off on some/all instances during this window, " +
        "or scrape labels do not match. Lifetime counters above are from Local Admin, not Prometheus."
      : "No samples in this range for the current filter. Check scrape config and traffic.";
    el.innerHTML = `<div class="card">
      <div class="card-head"><h2>${esc(title)} <span class="badge">last ${esc(range)}</span></h2>
        <a href="#/metrics">Open Metrics →</a></div>
      <p class="muted">${note}</p>
    </div>`;
    return;
  }

  const cards = list.map((p) => {
    const warn = p.warning ? `<p class="muted chart-warn">${esc(p.warning)}</p>` : "";
    return `<div class="card chart-card">
      <div class="card-head">
        <h2>${esc(p.title)} <span class="badge">${esc(series.range)}</span></h2>
        <span class="muted small">${esc(unitLabel(p.unit))}</span>
      </div>
      ${lineChartHtml(p.series || [], { unit: p.unit, height: 140, width: 520 })}
      ${warn}
    </div>`;
  }).join("");

  el.innerHTML = `<div class="card">
    <div class="card-head">
      <h2>${esc(title)} <span class="badge">last ${esc(range)}</span></h2>
      <a href="#/metrics">Open Metrics →</a>
    </div>
    <p class="muted small metrics-note">Window series from external metrics storage (not lifetime Admin counters).</p>
    <div class="grid-2 metrics-grid">${cards}</div>
  </div>`;
}

/**
 * Compact Overview card when metrics are connected (or a one-line note when disconnected).
 */
export async function metricsOverviewSectionHtml() {
  try {
    const status = await api("/api/metrics/status");
    if (status.status === "NotConfigured")
      return "";
    if (status.status === "Disconnected") {
      return `<div class="card">
        <div class="card-head">
          <h2>Metrics <span class="badge">history</span></h2>
          <a href="#/metrics">Open Metrics →</a>
        </div>
        <p class="muted" style="margin:0">Storage not connected${status.error ? `: ${esc(status.error)}` : "."}</p>
      </div>`;
    }

    const series = await api(
      "/api/metrics/series?range=1h&panels=request_rate,oc_hit_share,invalidation_rate");
    const byId = Object.fromEntries((series.panels || []).map((p) => [p.id, p]));
    const spark = (panel) => {
      if (!panel?.series?.length) return `<span class="muted">—</span>`;
      const merged = mergeSeriesPoints(panel.series);
      return lineChartHtml([{ name: "cluster", points: merged }], { unit: panel.unit, height: 100, width: 420 });
    };

    return `<div class="card">
      <div class="card-head">
        <h2>Metrics <span class="badge">last 1h</span></h2>
        <a href="#/metrics">Open Metrics →</a>
      </div>
      <div class="metrics-overview-grid">
        <div class="metrics-ov-block">
          <div class="label muted">Request rate</div>
          ${spark(byId.request_rate)}
        </div>
        <div class="metrics-ov-block">
          <div class="label muted">OC hit share</div>
          ${spark(byId.oc_hit_share)}
        </div>
        <div class="metrics-ov-block">
          <div class="label muted">Invalidations</div>
          ${spark(byId.invalidation_rate)}
        </div>
      </div>
    </div>`;
  } catch {
    return "";
  }
}

function mergeSeriesPoints(seriesList) {
  /** @type {Map<number, number>} */
  const map = new Map();
  for (const s of seriesList || []) {
    for (const p of s.points || []) {
      map.set(p.t, (map.get(p.t) || 0) + p.v);
    }
  }
  return [...map.entries()]
    .sort((a, b) => a[0] - b[0])
    .map(([t, v]) => ({ t, v }));
}

function unitLabel(unit) {
  if (unit === "percent") return "share 0–100%";
  if (unit === "rate") return "per second";
  if (unit === "ms") return "milliseconds";
  if (unit === "count") return "count";
  return unit || "";
}

function fmtRate(v) {
  if (v == null || Number.isNaN(v)) return "—";
  if (v >= 100) return num(Math.round(v));
  if (v >= 1) return v.toFixed(2);
  if (v >= 0.01) return v.toFixed(3);
  return v.toExponential(1);
}

function fmtShare(v) {
  if (v == null || Number.isNaN(v)) return "—";
  return pct(v);
}
