/**
 * Metrics page — time series from optional external Prometheus-compatible storage.
 */

import { api } from "./api.js";
import { bindChartExpand, lineChartHtml, updateChartInPlace } from "./charts.js";
import { $, main, mainHasContent, paintMain } from "./dom.js";
import { esc, num, pct } from "./format.js";
import { navigate, setBreadcrumb, setNavActive } from "./router.js";
import { bindEmptyStateActions, emptyStateHtml } from "./tables.js";

const RANGE_OPTS = ["15m", "1h", "6h", "24h", "7d"];

/** @type {Map<string, { title: string, series: Array, unit?: string }>} */
let lastPanelMap = new Map();

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
    const target = formatMetricsProvider(status);
    paint(`
      ${metricsStatusBanner(status)}
      <div class="card">${emptyStateHtml("metrics-offline", {
        title: `${target} · not connected`,
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
    const offline = { ...status, status: "Disconnected", error: series.error };
    paint(`
      ${metricsStatusBanner(offline)}
      <div class="card">${emptyStateHtml("metrics-offline", {
        title: `${formatMetricsProvider(offline)} · not connected`,
        detail: series.error || "Query failed.",
      })}</div>`, soft);
    bindEmptyStateActions(main());
    return;
  }

  lastPanelMap = buildPanelMap(series);

  // Soft path: patch live regions only — avoid remounting the whole chart grid.
  if (soft && $("#metricsLiveRoot") && $("#metricsRange")?.value === range) {
    const banner = $("#metricsBannerHost");
    if (banner) banner.innerHTML = metricsStatusBanner(status);
    softUpdateKpis(summary, series);
    softUpdateMetricsGrid(series);
    bindEmptyStateActions(main());
    ensureChartExpandBound();
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
  ensureChartExpandBound();
  $("#metricsToolbar")?.addEventListener("submit", (ev) => {
    ev.preventDefault();
    navigate("metrics", {
      range: $("#metricsRange")?.value || "1h",
      domains: ($("#metricsDomains")?.value || "").trim(),
    });
  });
}

function ensureChartExpandBound() {
  bindChartExpand(main(), () => lastPanelMap);
}

function paint(html, soft) {
  if (soft) paintMain(html);
  else main().innerHTML = html;
}

function softUpdateKpis(summary, series) {
  const root = $("#metricsKpis");
  if (!root) return;
  const vals = root.querySelectorAll("[data-kpi]");
  if (vals.length < 5) {
    root.innerHTML = metricsKpiHtml(summary, series);
    return;
  }
  const map = {
    req: fmtRate(summary?.requestRate),
    oc: fmtShare(summary?.ocHitShare),
    fc: fmtShare(summary?.fcHitRate),
    inv: fmtRate(summary?.invalidationRate),
    step: series?.step || "—",
  };
  for (const el of vals) {
    const key = el.getAttribute("data-kpi");
    if (key && map[key] != null && el.textContent !== String(map[key])) {
      el.textContent = map[key];
    }
  }
}

function softUpdateMetricsGrid(series) {
  const grid = $("#metricsGrid");
  if (!grid) return;
  const panels = series.panels || [];
  if (!panels.length) {
    if (!grid.querySelector(".chart-card")) {
      grid.innerHTML = `<div class="card">${emptyStateHtml("metrics-empty")}</div>`;
    }
    return;
  }

  // Ensure cards exist for each panel id (first paint structure).
  const existing = new Set([...grid.querySelectorAll(".chart-card")].map((c) => c.dataset.panel));
  const wanted = panels.map((p) => p.id);
  const structureOk = wanted.length === existing.size && wanted.every((id) => existing.has(id));
  if (!structureOk) {
    grid.innerHTML = metricsPanelsHtml(series);
    return;
  }

  for (const p of panels) {
    const card = grid.querySelector(`.chart-card[data-panel="${cssEscape(p.id)}"]`);
    if (!card) continue;
    const badge = card.querySelector("[data-chart-range]");
    if (badge) badge.textContent = series.range || "";
    const host = card.querySelector("[data-chart-host]");
    if (host) {
      updateChartInPlace(host, p.series || [], { unit: p.unit, height: 200, width: 640 });
    }
    let warn = card.querySelector(".chart-warn");
    if (p.warning) {
      if (!warn) {
        warn = document.createElement("p");
        warn.className = "muted chart-warn";
        card.appendChild(warn);
      }
      if (warn.textContent !== p.warning) warn.textContent = p.warning;
    } else if (warn) {
      warn.remove();
    }
  }
}

function cssEscape(id) {
  // data-panel values are allowlisted ids (snake_case); avoid CSS.escape dependency.
  return String(id).replace(/"/g, "");
}

function metricsKpiHtml(summary, series) {
  return `
      <div class="kpi" title="Request rate over the selected metrics window"><div class="label">Req / s</div><div class="value" data-kpi="req">${fmtRate(summary?.requestRate)}</div></div>
      <div class="kpi" title="Output Cache hit share over the selected metrics window"><div class="label">OC hit (window)</div><div class="value" data-kpi="oc">${fmtShare(summary?.ocHitShare)}</div></div>
      <div class="kpi" title="Fusion layer hit rate over the selected metrics window"><div class="label">FC hit rate</div><div class="value" data-kpi="fc">${fmtShare(summary?.fcHitRate)}</div></div>
      <div class="kpi" title="Invalidation rate over the selected metrics window"><div class="label">Inv / s</div><div class="value" data-kpi="inv">${fmtRate(summary?.invalidationRate)}</div></div>
      <div class="kpi"><div class="label">Step</div><div class="value" data-kpi="step" style="font-size:1rem">${esc(series?.step || "—")}</div></div>`;
}

function metricsPanelsHtml(series) {
  const panelCards = (series.panels || []).map((p) => panelCardHtml(p, series.range)).join("");
  return panelCards || `<div class="card">${emptyStateHtml("metrics-empty")}</div>`;
}

function panelCardHtml(p, range, chartOpts = {}) {
  const height = chartOpts.height || 200;
  const width = chartOpts.width || 640;
  const warn = p.warning
    ? `<p class="muted chart-warn">${esc(p.warning)}</p>`
    : "";
  const tip = (p.description && String(p.description).trim()) || "";
  return `
      <div class="card chart-card" data-panel="${esc(p.id)}">
        <div class="card-head">
          <h2${tip ? ` title="${esc(tip)}"` : ""}>${esc(p.title)} <span class="badge" data-chart-range>${esc(range || "")}</span></h2>
          <div class="chart-card-actions">
            <span class="muted small" title="Y-axis unit for this series">${esc(unitLabel(p.unit))}</span>
            <button type="button" class="secondary chart-expand-btn" data-chart-expand="${esc(p.id)}" title="Enlarge chart">⛶</button>
          </div>
        </div>
        <div data-chart-host>${lineChartHtml(p.series || [], { unit: p.unit, height, width })}</div>
        ${warn}
      </div>`;
}

function buildPanelMap(series) {
  /** @type {Map<string, { title: string, description?: string, series: Array, unit?: string }>} */
  const map = new Map();
  for (const p of series?.panels || []) {
    map.set(p.id, {
      title: p.title,
      description: p.description,
      series: p.series || [],
      unit: p.unit,
    });
  }
  return map;
}

/**
 * Provider name · base host for status UI (always show target when configured).
 * @param {{ provider?: string, host?: string }|null|undefined} status
 */
export function formatMetricsProvider(status) {
  const provider = status?.provider || "Prometheus";
  const host = status?.host ? String(status.host).trim() : "";
  return host ? `${provider} · ${host}` : provider;
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
        ${esc(status.provider || "Prometheus")}${status.host ? ` · <code>${esc(status.host)}</code>` : ""}
        · not connected${status.error ? ` — ${esc(status.error)}` : ""}
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
  const soft = el.dataset.metricsReady === "1" && el.querySelector(".metrics-grid");

  let status;
  try {
    status = await api("/api/metrics/status");
  } catch {
    if (!soft) el.innerHTML = "";
    return;
  }

  if (status.status === "NotConfigured") {
    if (!soft) el.innerHTML = "";
    return;
  }
  if (status.status === "Disconnected") {
    if (soft) return;
    el.innerHTML = `<div class="card"><h2>Metrics <span class="badge">last ${esc(range)}</span></h2>
      <p class="muted">${esc(formatMetricsProvider(status))} · not connected${status.error ? `: ${esc(status.error)}` : "."}</p>
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
    if (soft) return;
    el.innerHTML = `<div class="card"><h2>Metrics</h2><p class="muted">${esc(err.message)}</p></div>`;
    return;
  }

  if (series.status !== "Connected") {
    if (soft) return;
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
    if (soft) return;
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

  const panelMap = buildPanelMap(series);
  lastPanelMap = new Map([...lastPanelMap, ...panelMap]);

  if (soft) {
    const grid = el.querySelector(".metrics-grid");
    if (grid) {
      for (const p of list) {
        const card = grid.querySelector(`.chart-card[data-panel="${cssEscape(p.id)}"]`);
        const host = card?.querySelector("[data-chart-host]");
        if (host) updateChartInPlace(host, p.series || [], { unit: p.unit, height: 180, width: 560 });
      }
      ensureChartExpandBound();
      return;
    }
  }

  const cards = list.map((p) => panelCardHtml(p, series.range, { height: 180, width: 560 })).join("");

  el.dataset.metricsReady = "1";
  el.innerHTML = `<div class="card">
    <div class="card-head">
      <h2>${esc(title)} <span class="badge">last ${esc(range)}</span></h2>
      <a href="#/metrics">Open Metrics →</a>
    </div>
    <p class="muted small metrics-note">Window series from external metrics storage (not lifetime Admin counters).</p>
    <div class="grid-2 metrics-grid">${cards}</div>
  </div>`;
  ensureChartExpandBound();
}

/**
 * Compact Overview card when metrics are connected (or a one-line note when disconnected).
 * @param {{ soft?: boolean }} [opts]
 */
export async function metricsOverviewSectionHtml(opts = {}) {
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
        <p class="muted" style="margin:0">${esc(formatMetricsProvider(status))} · not connected${status.error ? `: ${esc(status.error)}` : "."}</p>
      </div>`;
    }

    const series = await api(
      "/api/metrics/series?range=1h&panels=request_rate,oc_hit_share,invalidation_rate");
    const byId = Object.fromEntries((series.panels || []).map((p) => [p.id, p]));
    const spark = (panel) => {
      if (!panel?.series?.length) return `<span class="muted">—</span>`;
      const merged = mergeSeriesPoints(panel.series);
      return `<div data-chart-host class="metrics-ov-chart">${lineChartHtml([{ name: "cluster", points: merged }], { unit: panel.unit, height: 120, width: 480 })}</div>`;
    };

    // Soft path caller may patch hosts instead of replacing the whole card.
    if (opts.soft && opts.mountEl) {
      const mount = opts.mountEl;
      if (mount.querySelector(".metrics-overview-grid")) {
        softPatchOverviewSparks(mount, byId);
        return null; // signal: already patched
      }
    }

    const ovLabel = (panel, fallback) => {
      const tip = panel?.description && String(panel.description).trim();
      return `<div class="label muted"${tip ? ` title="${esc(tip)}"` : ""}>${esc(panel?.title || fallback)}</div>`;
    };
    return `<div class="card" data-ov-metrics-card>
      <div class="card-head">
        <h2 title="Windowed series from external metrics storage (not lifetime Admin counters)">Metrics <span class="badge">last 1h</span></h2>
        <a href="#/metrics">Open Metrics →</a>
      </div>
      <div class="metrics-overview-grid">
        <div class="metrics-ov-block" data-ov-spark="request_rate">
          ${ovLabel(byId.request_rate, "Request rate")}
          ${spark(byId.request_rate)}
        </div>
        <div class="metrics-ov-block" data-ov-spark="oc_hit_share">
          ${ovLabel(byId.oc_hit_share, "OC hit share")}
          ${spark(byId.oc_hit_share)}
        </div>
        <div class="metrics-ov-block" data-ov-spark="invalidation_rate">
          ${ovLabel(byId.invalidation_rate, "Invalidations")}
          ${spark(byId.invalidation_rate)}
        </div>
      </div>
    </div>`;
  } catch {
    return "";
  }
}

function softPatchOverviewSparks(mount, byId) {
  for (const id of ["request_rate", "oc_hit_share", "invalidation_rate"]) {
    const block = mount.querySelector(`[data-ov-spark="${id}"]`);
    const host = block?.querySelector("[data-chart-host]");
    const panel = byId[id];
    if (!host || !panel?.series?.length) continue;
    const merged = mergeSeriesPoints(panel.series);
    updateChartInPlace(host, [{ name: "cluster", points: merged }], {
      unit: panel.unit,
      height: 120,
      width: 480,
    });
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
