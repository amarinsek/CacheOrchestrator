/**
 * Metrics page — time series from optional external Prometheus-compatible storage.
 */

import { api } from "./api.js";
import {
  bindChartExpand,
  lineChartHtml,
  refreshOpenChartModal,
  seriesHasSamples,
  updateChartInPlace,
} from "./charts.js";
import { $, beginPageLoad, main, mainHasContent, paintPage } from "./dom.js";
import {
  applyButtonHtml,
  bindMultiSelects,
  csvParamFromSelection,
  multiSelectHtml,
  parseCsvParam,
  readMultiSelect,
} from "./filters.js";
import { esc, METRIC_TITLES, num, pct } from "./format.js";
import { navigate, setBreadcrumb, setNavActive } from "./router.js";
import { bindEmptyStateActions, emptyStateHtml } from "./tables.js";
import {
  appendMetricsRangeParams,
  chartWindow,
  getMetricsQueryArgs,
  getPromRange,
  setFromSelectValue,
  setMetricsCapability,
} from "./time-range.js";

/**
 * Chart opts that force X-axis to the selected range window (relative or absolute).
 * Prefer series fromUtc/toUtc when the API returns them.
 * @param {string} range
 * @param {{ height?: number, width?: number, unit?: string, queriedAtUtc?: string|null, fromUtc?: string|null, toUtc?: string|null, step?: string|null }} [extra]
 */
function chartOptsForRange(range, extra = {}) {
  const step = extra.step || null;
  if (extra.fromUtc && extra.toUtc) {
    const tMin = Math.floor(new Date(extra.fromUtc).getTime() / 1000);
    const tMax = Math.floor(new Date(extra.toUtc).getTime() / 1000);
    if (Number.isFinite(tMin) && Number.isFinite(tMax) && tMax > tMin) {
      return {
        unit: extra.unit,
        height: extra.height,
        width: extra.width,
        range: range || "custom",
        tMin,
        tMax,
        step,
      };
    }
  }
  const toSec = extra.queriedAtUtc
    ? Math.floor(new Date(extra.queriedAtUtc).getTime() / 1000)
    : Math.floor(Date.now() / 1000);
  const win = chartWindow(range || "1h", Number.isFinite(toSec) ? toSec : undefined);
  return {
    unit: extra.unit,
    height: extra.height,
    width: extra.width,
    range: win.range,
    tMin: win.tMin,
    tMax: win.tMax,
    step,
  };
}

/** @type {Map<string, { title: string, series: Array, unit?: string }>} */
let lastPanelMap = new Map();

/**
 * @param {URLSearchParams} params
 * @param {{ soft?: boolean }} [opts]
 */
export async function renderMetrics(params = new URLSearchParams(), opts = {}) {
  const soft = !!opts.soft;
  setNavActive("metrics");
  setBreadcrumb([]);
  beginPageLoad(soft, `<div class="card"><p class="muted">Loading metrics…</p></div>`);

  let status;
  try {
    status = await api("/api/metrics/status");
  } catch (err) {
    if (soft && mainHasContent()) return;
    paintPage(`<div class="card">${emptyStateHtml("error", {
      title: "Cannot load metrics status",
      detail: err.message,
    })}</div>`, soft);
    bindEmptyStateActions(main());
    return;
  }

  if (status.status === "NotConfigured") {
    setMetricsCapability("not_configured");
    paintPage(`
      <div class="card">${emptyStateHtml("metrics-config", {
        title: "Metrics not configured",
        detail: status.error
          || "Set AdminConsole:Metrics (Enabled, Provider, BaseUrl) to enable charts.",
        actions: [
          { label: "Refresh", onclick: "window.__adminRefresh && window.__adminRefresh()" },
          { label: "Overview", href: "#/overview" },
          { label: "Instances", href: "#/instances" },
        ],
      })}</div>`, soft);
    bindEmptyStateActions(main());
    return;
  }

  if (status.status === "Disconnected") {
    setMetricsCapability("disconnected");
    const target = formatMetricsProvider(status);
    paintPage(`
      <div class="card">${emptyStateHtml("metrics-offline", {
        title: `${target} · not connected`,
        detail: status.error || "Could not reach the metrics backend. Check URL, network, and credentials.",
        actions: [
          { label: "Refresh", onclick: "window.__adminRefresh && window.__adminRefresh()" },
          { label: "Instances", href: "#/instances" },
        ],
      })}</div>`, soft);
    bindEmptyStateActions(main());
    return;
  }

  setMetricsCapability("connected");
  // Prefer global Range picker; URL ?range= still works for relative shortcuts.
  if (params.get("range") && params.get("range") !== "custom") {
    setFromSelectValue(params.get("range"));
  }

  const selDomains = parseCsvParam(params, "domains");
  // Domain filter options from window stats (or config fan-out as fallback names).
  let domainOpts = [];
  try {
    const cfg = await api("/api/domains");
    domainOpts = (cfg.data || []).map((d) => ({ id: d.name, label: d.name }));
  } catch { /* empty multi-select */ }

  let summary = null;
  let series = null;
  try {
    // Explicit "None": empty charts without Prom call.
    if (selDomains !== null && selDomains.length === 0) {
      summary = { status: "Connected", range: getMetricsQueryArgs().range, noData: true };
      series = { status: "Connected", range: getMetricsQueryArgs().range, panels: [], step: "—" };
    } else {
      const seriesQ = appendMetricsRangeParams(new URLSearchParams());
      if (selDomains?.length) seriesQ.set("domains", selDomains.join(","));
      const summaryQ = appendMetricsRangeParams(new URLSearchParams());
      [summary, series] = await Promise.all([
        api("/api/metrics/summary?" + summaryQ.toString()),
        api("/api/metrics/series?" + seriesQ.toString()),
      ]);
    }
  } catch (err) {
    if (soft && mainHasContent()) return;
    paintPage(`<div class="card">${emptyStateHtml("error", { detail: err.message })}</div>`, soft);
    bindEmptyStateActions(main());
    return;
  }

  if (series.status === "Disconnected") {
    paintPage(`
      <div class="card">${emptyStateHtml("metrics-offline", {
        title: `${formatMetricsProvider(status)} · not connected`,
        detail: series.error || "Query failed.",
        actions: [
          { label: "Refresh", onclick: "window.__adminRefresh && window.__adminRefresh()" },
          { label: "Instances", href: "#/instances" },
        ],
      })}</div>`, soft);
    bindEmptyStateActions(main());
    return;
  }

  lastPanelMap = buildPanelMap(series);
  const resolvedRange = series.range || getMetricsQueryArgs().range || "1h";
  const windowKey = metricsWindowKey(series);

  // Soft path only when the same time window is already shown — range change must full-repaint charts.
  const liveRoot = $("#metricsLiveRoot");
  const shownKey = liveRoot?.dataset?.metricsWindow;
  if (soft && liveRoot && shownKey === windowKey) {
    softUpdateKpis(summary, series);
    softUpdateMetricsGrid(series);
    bindEmptyStateActions(main());
    ensureChartExpandBound();
    refreshOpenChartModal();
    return;
  }

  paintPage(`
    <form class="toolbar" id="metricsToolbar">
      ${multiSelectHtml("metDom", "Domains", domainOpts, selDomains)}
      ${applyButtonHtml()}
    </form>
    <div id="metricsLiveRoot" data-metrics-range="${esc(resolvedRange)}" data-metrics-window="${esc(windowKey)}">
      <div class="grid-2 metrics-grid" id="metricsGrid">
        ${metricsPanelsHtml(series)}
      </div>
    </div>`, soft);

  bindEmptyStateActions(main());
  bindMultiSelects(main());
  ensureChartExpandBound();
  refreshOpenChartModal();
  $("#metricsToolbar")?.addEventListener("submit", (ev) => {
    ev.preventDefault();
    const form = ev.target;
    navigate("metrics", {
      domains: csvParamFromSelection(readMultiSelect(form, "metDom")),
    });
  });
}

function metricsWindowKey(series) {
  if (series?.fromUtc && series?.toUtc) return `abs:${series.fromUtc}|${series.toUtc}`;
  return `rel:${series?.range || getMetricsQueryArgs().range || "1h"}`;
}

function ensureChartExpandBound() {
  bindChartExpand(main(), () => {
    const map = new Map();
    for (const [id, data] of lastPanelMap) {
      const win = chartOptsForRange(data.range || getPromRange() || "1h", {
        unit: data.unit,
        queriedAtUtc: data.queriedAtUtc,
        fromUtc: data.fromUtc,
        toUtc: data.toUtc,
        step: data.step,
      });
      map.set(id, { ...data, ...win, panelId: id });
    }
    return map;
  });
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

  const baseOpts = chartOptsForRange(series.range || "1h", {
    height: 200,
    width: 640,
    queriedAtUtc: series.queriedAtUtc,
    fromUtc: series.fromUtc,
    toUtc: series.toUtc,
    step: series.step,
  });

  softUpdateChartCards(grid, panels, baseOpts, { syncWarnings: true });
}

/**
 * Soft in-place chart patch shared by Metrics page, detail mounts, and Overview embed.
 * @param {HTMLElement} grid
 * @param {Array} panels
 * @param {object} baseOpts from chartOptsForRange (unit applied per panel)
 * @param {{ syncWarnings?: boolean }} [opts]
 */
function softUpdateChartCards(grid, panels, baseOpts, opts = {}) {
  for (const p of panels) {
    const card = grid.querySelector(`.chart-card[data-panel="${cssEscape(p.id)}"]`);
    if (!card) continue;
    const host = card.querySelector("[data-chart-host]");
    if (host) {
      updateChartInPlace(host, p.series || [], { ...baseOpts, unit: p.unit });
    }
    if (!opts.syncWarnings) continue;
    let warn = card.querySelector(".chart-warn");
    // Suppress series-empty warnings — empty axes + badge cover that case.
    const showWarn = p.warning && seriesHasSamples(p.series);
    if (showWarn) {
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
      <div class="kpi" title="${esc(METRIC_TITLES.rpsWindow)}"><div class="label">RPS</div><div class="value" data-kpi="req">${fmtRate(summary?.requestRate)}</div></div>
      <div class="kpi" title="${esc(METRIC_TITLES.ocHitShare)}"><div class="label">OC hit %</div><div class="value" data-kpi="oc">${fmtShare(summary?.ocHitShare)}</div></div>
      <div class="kpi" title="${esc(METRIC_TITLES.fcHitRate)}"><div class="label">FC hit rate</div><div class="value" data-kpi="fc">${fmtShare(summary?.fcHitRate)}</div></div>
      <div class="kpi" title="${esc(METRIC_TITLES.invRate)}"><div class="label">Inv / s</div><div class="value" data-kpi="inv">${fmtRate(summary?.invalidationRate)}</div></div>
      <div class="kpi"><div class="label">Step</div><div class="value" data-kpi="step" style="font-size:1rem">${esc(series?.step || "—")}</div></div>`;
}

function metricsPanelsHtml(series) {
  const panelCards = (series.panels || []).map((p) =>
    panelCardHtml(p, series.range, {
      height: 200,
      width: 640,
      queriedAtUtc: series.queriedAtUtc,
      fromUtc: series.fromUtc,
      toUtc: series.toUtc,
      step: series.step,
    })).join("");
  return panelCards || `<div class="card">${emptyStateHtml("metrics-empty")}</div>`;
}

function panelCardHtml(p, range, chartOpts = {}) {
  const height = chartOpts.height || 200;
  const width = chartOpts.width || 640;
  const opts = chartOptsForRange(range || "1h", {
    unit: p.unit,
    height,
    width,
    queriedAtUtc: chartOpts.queriedAtUtc,
    fromUtc: chartOpts.fromUtc,
    toUtc: chartOpts.toUtc,
    step: chartOpts.step,
  });
  const hasSamples = seriesHasSamples(p.series);
  // Query warnings that only mean “empty matrix” are replaced by the no-samples badge.
  const warn = p.warning && hasSamples
    ? `<p class="muted chart-warn">${esc(p.warning)}</p>`
    : "";
  const tip = (p.description && String(p.description).trim()) || "";
  const titleTip = tip || p.title || "";
  return `
      <div class="card chart-card" data-panel="${esc(p.id)}">
        <div class="card-head">
          <div class="chart-card-title">
            <h2 title="${esc(titleTip)}">${esc(p.title)}</h2>
          </div>
          <div class="chart-card-actions">
            <span class="muted small chart-unit" title="Y-axis unit for this series">${esc(unitLabel(p.unit))}</span>
            <button type="button" class="secondary chart-expand-btn" data-chart-expand="${esc(p.id)}" title="Enlarge chart">⛶</button>
          </div>
        </div>
        <div data-chart-host>${lineChartHtml(p.series || [], opts)}</div>
        ${warn}
      </div>`;
}

function buildPanelMap(series) {
  /** @type {Map<string, object>} */
  const map = new Map();
  for (const p of series?.panels || []) {
    map.set(p.id, {
      title: p.title,
      description: p.description,
      series: p.series || [],
      unit: p.unit,
      range: series?.range,
      queriedAtUtc: series?.queriedAtUtc,
      fromUtc: series?.fromUtc,
      toUtc: series?.toUtc,
      step: series?.step,
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
        Time series require <code>AdminConsole:Metrics</code> (Enabled, Provider, BaseUrl).
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

  const mq = getMetricsQueryArgs();
  const range = opts.range || mq.range || "1h";
  const windowKey = mq.from && mq.to ? `abs:${mq.from}|${mq.to}` : `rel:${range}`;
  let soft = el.dataset.metricsReady === "1" && el.querySelector(".metrics-grid");
  // Range change on detail pages must rebuild charts (X-axis window).
  if (soft && el.dataset.metricsWindow && el.dataset.metricsWindow !== windowKey) {
    soft = false;
  }

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
    el.innerHTML = `<p class="muted">${esc(formatMetricsProvider(status))} · not connected${status.error ? `: ${esc(status.error)}` : "."}</p>`;
    return;
  }

  const panels = opts.scope === "domain"
    ? "request_rate,oc_hit_share,fc_hit_rate,invalidation_rate,schedule_phase,fc_p95_ms"
    : opts.scope === "instance"
      ? "request_rate,oc_hit_share,fc_hit_rate,invalidation_rate,fc_p95_ms,cluster_publish_failures"
      : "request_rate,oc_hit_share,fc_hit_rate,factory_share,factory_p95_ms,fc_p95_ms";

  const q = appendMetricsRangeParams(new URLSearchParams({ panels }));
  if (opts.range && !mq.from) q.set("range", opts.range);
  if (opts.domain) q.set("domains", opts.domain);
  if (opts.instanceId) q.set("instances", opts.instanceId);
  if (opts.route) q.set("routes", opts.route);

  let series;
  try {
    series = await api("/api/metrics/series?" + q.toString());
  } catch (err) {
    if (soft) return;
    el.innerHTML = `<p class="muted">${esc(err.message)}</p>`;
    return;
  }

  if (series.status !== "Connected") {
    if (soft) return;
    el.innerHTML = `<p class="muted">${esc(series.error || "Not connected")}</p>`;
    return;
  }

  const resolvedRange = series.range || range;
  const list = series.panels || [];

  const panelMap = buildPanelMap(series);
  lastPanelMap = new Map([...lastPanelMap, ...panelMap]);

  if (soft && el.dataset.metricsWindow === windowKey) {
    const grid = el.querySelector(".metrics-grid");
    if (grid) {
      const baseOpts = chartOptsForRange(resolvedRange, {
        height: 200,
        width: 640,
        queriedAtUtc: series.queriedAtUtc,
        fromUtc: series.fromUtc,
        toUtc: series.toUtc,
        step: series.step,
      });
      softUpdateChartCards(grid, list, baseOpts);
      ensureChartExpandBound();
      refreshOpenChartModal();
      return;
    }
  }

  const cards = list.map((p) => panelCardHtml(p, resolvedRange, {
    height: 200,
    width: 640,
    queriedAtUtc: series.queriedAtUtc,
    fromUtc: series.fromUtc,
    toUtc: series.toUtc,
    step: series.step,
  })).join("");

  el.dataset.metricsReady = "1";
  el.dataset.metricsRange = resolvedRange;
  el.dataset.metricsWindow = windowKey;
  el.innerHTML = `<div class="grid-2 metrics-grid">${cards || emptyStateHtml("metrics-empty")}</div>`;
  ensureChartExpandBound();
  refreshOpenChartModal();
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
        </div>
        <p class="muted" style="margin:0">${esc(formatMetricsProvider(status))} · not connected${status.error ? `: ${esc(status.error)}` : "."}</p>
      </div>`;
    }

    const q = appendMetricsRangeParams(new URLSearchParams({
      panels: "request_rate,oc_hit_share,invalidation_rate",
    }));
    const series = await api(`/api/metrics/series?${q.toString()}`);
    const resolvedRange = series.range || getMetricsQueryArgs().range || "1h";
    const windowKey = metricsWindowKey(series);
    const list = series.panels || [];

    // Same chart cards as Metrics / detail (expand, axes, no-samples badge).
    const panelMap = buildPanelMap(series);
    lastPanelMap = new Map([...lastPanelMap, ...panelMap]);

    // Soft path only when the same range is already shown.
    if (opts.soft && opts.mountEl) {
      const mount = opts.mountEl;
      const root = mount.querySelector("[data-ov-metrics-card]");
      const grid = mount.querySelector("#ovMetricsGrid");
      if (root && root.dataset.metricsWindow === windowKey && grid?.querySelector(".chart-card")) {
        const baseOpts = chartOptsForRange(resolvedRange, {
          height: 200,
          width: 640,
          queriedAtUtc: series.queriedAtUtc,
          fromUtc: series.fromUtc,
          toUtc: series.toUtc,
          step: series.step,
        });
        softUpdateChartCards(grid, list, baseOpts);
        ensureChartExpandBound();
        refreshOpenChartModal();
        return null; // signal: already patched
      }
    }

    const cards = list.map((p) => panelCardHtml(p, resolvedRange, {
      height: 200,
      width: 640,
      queriedAtUtc: series.queriedAtUtc,
      fromUtc: series.fromUtc,
      toUtc: series.toUtc,
      step: series.step,
    })).join("");

    // Defer expand bind to caller after mount is in DOM (overview paints then).
    queueMicrotask(() => {
      ensureChartExpandBound();
      refreshOpenChartModal();
    });

    return `<div data-ov-metrics-card data-metrics-range="${esc(resolvedRange)}" data-metrics-window="${esc(windowKey)}">
      <div class="grid-2 metrics-grid" id="ovMetricsGrid">${cards || emptyStateHtml("metrics-empty")}</div>
    </div>`;
  } catch {
    return "";
  }
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
