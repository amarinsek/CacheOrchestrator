/**
 * Sticky chrome: cluster metrics header + Grafana-style auto-refresh.
 *
 * Layout (see index.html):
 * 1) brand / logo
 * 2) header metrics strip (`#headerMetrics`):
 *    instances + metrics status · gap · Req / Inv / pipeline / OC / DC / FA run / FAFC / FAD / DC stale / EFTS · right-aligned N dom N ep
 * 3) menu strip
 *
 * Soft refresh (route({ soft: true })) repaints without a full "Loading…" flash.
 * Concurrent soft refreshes coalesce to the latest run.
 */

import { api } from "./api.js";
import { $, setRefreshing } from "./dom.js";
import { esc, fmtUnit, fmtDurationMs, METRIC_TITLES, noDataHtml, num, pct, pipelineBar } from "./format.js";
import { severityStack } from "./hints.js";
import {
  appendMetricsRangeParams,
  setMetricsCapability,
  setTimeRangeControlsEnabled,
} from "./time-range.js";
import { instancesUpClass } from "./views-shared.js";

const REFRESH_KEY = "adminAutoRefreshSec";

/** Live page forces 5s auto-refresh and locks Range / interval pickers. */
const LIVE_REFRESH_SEC = 5;

let headerTimer = null;
let pageTimer = null;
/** Coalesce overlapping soft refreshes (auto-refresh + manual). */
let softRefreshBusy = false;
let softRefreshAgain = false;
/** When true, Range + interval pickers are disabled and interval is fixed at 5s. */
let liveChromeLock = false;
/** User interval (localStorage) restored when leaving Live. */
let savedRefreshBeforeLive = null;
/**
 * Last successful overview payload.
 * Note: ES module live bindings are read-only to importers — use setLastOverview().
 */
let lastOverview = null;

export function getLastOverview() {
  return lastOverview;
}

export function setLastOverview(o) {
  lastOverview = o;
}

/** Injected from app.js to avoid circular import with views. */
let routeHandler = null;

export function setRouteHandler(fn) {
  routeHandler = fn;
}

/** Allowed auto-refresh intervals (seconds), Grafana-style plus Off. */
const AUTO_REFRESH_SEC = [0, 5, 10, 30, 60, 300, 900, 1800, 3600];

export function getAutoRefreshSec() {
  const v = Number(localStorage.getItem(REFRESH_KEY) || "0");
  return AUTO_REFRESH_SEC.includes(v) ? v : 0;
}

export function setAutoRefreshSec(sec) {
  localStorage.setItem(REFRESH_KEY, String(sec));
  scheduleRefresh();
}

/**
 * Live page: disable Range + auto-refresh interval pickers; force soft refresh every 5s.
 * Leaving Live re-enables pickers and restores the previous interval (localStorage).
 * Manual Reload stays available.
 * @param {boolean} on
 */
export function setLiveChromeMode(on) {
  const sel = $("#selAutoRefresh");
  const refreshHost = document.querySelector(".nav-refresh");

  if (on) {
    if (!liveChromeLock) {
      savedRefreshBeforeLive = getAutoRefreshSec();
      liveChromeLock = true;
    }
    if (sel) {
      sel.value = String(LIVE_REFRESH_SEC);
      sel.disabled = true;
      sel.title = "Fixed at 5s on Live (lookback is always 1m)";
    }
    refreshHost?.classList.add("is-live-locked");
    setTimeRangeControlsEnabled(false, {
      reason: "Time range is fixed on Live (1m lookback)",
    });
    scheduleRefresh();
    return;
  }

  if (!liveChromeLock) return;
  liveChromeLock = false;
  const restore = savedRefreshBeforeLive ?? 0;
  savedRefreshBeforeLive = null;
  if (sel) {
    sel.disabled = false;
    sel.removeAttribute("title");
    sel.value = String(restore);
  }
  refreshHost?.classList.remove("is-live-locked");
  setTimeRangeControlsEnabled(true);
  setAutoRefreshSec(restore);
}

/**
 * Lightweight header-only refresh (overview API + metrics status pill).
 * @param {{ silent?: boolean }} [opts] silent: do not flash error over a good header mid-refresh
 */
export async function refreshHeader(opts = {}) {
  const silent = !!opts.silent;
  try {
    const o = await api("/api/overview");
    setLastOverview(o);
    const windowStats = await fetchHeaderWindowStats();
    renderHeader(o, windowStats);
    updateNavHintsBadge(
      windowStats?.status === "Connected" && windowStats.hintSummary
        ? windowStats.hintSummary
        : o.hintSummary);
  } catch (err) {
    if (silent && lastOverview) {
      return;
    }
    setLastOverview(null);
    $("#headerMetrics").innerHTML = `
      <span class="hm status-Down" title="${esc(err.message)}">Admin API error</span>
      <span class="hm muted">${esc(err.message)}</span>`;
    updateNavHintsBadge({ total: 0 });
  }
}

/** Traffic KPIs for the chrome strip (Prometheus only). */
async function fetchHeaderWindowStats() {
  try {
    const q = appendMetricsRangeParams(new URLSearchParams());
    const w = await api("/api/stats/window?" + q.toString());
    if (w?.status === "Connected") setMetricsCapability("connected");
    else if (w?.status === "Disconnected") setMetricsCapability("disconnected");
    else if (w?.status === "NotConfigured") setMetricsCapability("not_configured");
    return w;
  } catch {
    setMetricsCapability("disconnected");
    return null;
  }
}

/** Metrics store status pill HTML (empty when not configured). */
function metricsStatusPillHtml(s) {
  if (!s || s.status === "NotConfigured") return "";
  const provider = s.provider || "Prometheus";
  const target = s.host ? `${provider} · ${s.host}` : provider;
  const cls = s.status === "Connected" ? "ok" : "bad";
  const title = s.status === "Connected"
    ? target
    : `${target} · not connected${s.error ? ` — ${s.error}` : ""}`;
  const label = s.status === "Connected" ? "metrics up" : "metrics down";
  return `<span class="hm" data-metrics-pill title="${esc(title)}"><span class="dot ${cls}"></span><span class="muted">${esc(label)}</span></span>`;
}

function setMetricsStatusPill(s) {
  const slot = document.querySelector("[data-metrics-pill-slot]");
  if (!slot) return;
  slot.innerHTML = metricsStatusPillHtml(s);
}

/** Non-blocking metrics store status for the chrome strip (refines host tooltip). */
async function refreshMetricsStatusPill() {
  try {
    setMetricsStatusPill(await api("/api/metrics/status"));
  } catch {
    /* optional — keep the window-stats pill if already rendered */
  }
}

/**
 * Soft refresh: fetch in background, repaint without "Loading…" flash.
 * Concurrent calls coalesce to one extra pass with latest data.
 */
export async function refreshAll() {
  if (softRefreshBusy) {
    softRefreshAgain = true;
    return;
  }
  softRefreshBusy = true;
  setRefreshing(true);
  try {
    do {
      softRefreshAgain = false;
      if (routeHandler) await routeHandler({ soft: true });
    } while (softRefreshAgain);
  } finally {
    softRefreshBusy = false;
    setRefreshing(false);
  }
}

/** Expose for empty-state buttons (`data-es-refresh`). */
if (typeof window !== "undefined") {
  window.__adminRefresh = refreshAll;
}

export function updateNavHintsBadge(summary) {
  const el = document.querySelector("[data-nav-hints-badge]");
  if (!el) return;
  el.innerHTML = severityStack(summary || { total: 0 });
}

/**
 * Render cluster metrics into `#headerMetrics`.
 * Health from Admin overview; traffic KPIs from Prometheus window stats.
 * @param {object} o overview DTO (instances / health)
 * @param {object|null} [windowStats] /api/stats/window payload
 */
export function renderHeader(o, windowStats = null) {
  const total = (o.instances || []).length
    || ((o.healthyCount || 0) + (o.degradedCount || 0) + (o.downCount || 0));
  const up = o.healthyCount ?? 0;
  const down = o.downCount ?? 0;
  const deg = o.degradedCount ?? 0;
  const upClass = instancesUpClass(o);
  const healthDots = [
    ...Array(o.healthyCount || 0).fill("ok"),
    ...Array(o.degradedCount || 0).fill("warn"),
    ...Array(o.downCount || 0).fill("bad"),
  ].map((c) => `<span class="dot ${c}"></span>`).join("") || `<span class="muted">—</span>`;

  const promOk = windowStats && windowStats.status === "Connected";
  const noData = promOk && windowStats.noData;
  const healthTitle = [
    `${up}/${total} healthy`,
    o.degradedCount ? `${o.degradedCount} degraded` : null,
    o.downCount ? `${o.downCount} down` : null,
  ].filter(Boolean).join(" · ");

  const pipe = promOk && !noData ? windowStats.pipeline : null;
  const dashTip = promOk ? "No samples yet" : "Metrics offline";
  const share = (v) => (promOk && !noData && v != null ? pct(v) : noDataHtml(dashTip));
  // Exclusive mix: OC hit · DC hit · FA run. DC stale is an overlay.
  const oc = share(pipe?.outputCacheHitShare ?? windowStats.outputCacheHitShare);
  const fc = share(pipe?.dataCacheHitShare ?? windowStats.dataCacheHitShare);
  const stale = share(pipe?.staleShare);
  const fac = share(pipe?.factoryShare ?? pipe?.originShare ?? windowStats.factoryShare);
  const fafcFails = promOk && !noData
    ? (windowStats.domains || []).reduce((s, d) => s + Number(d.dataCache?.factoryFailures || 0), 0)
    : null;
  const fafcRuns = promOk && !noData
    ? (windowStats.domains || []).reduce((s, d) => s + Number(d.dataCache?.factoryRuns || 0), 0)
    : 0;
  const fafcRate = fafcRuns > 0 && fafcFails != null ? fafcFails / fafcRuns : null;
  const fafcCls = fafcFails > 0
    ? (fafcRate != null && fafcRate >= 0.1 ? "metric-bad" : "metric-warn")
    : "";
  const fafc = fafcFails != null
    ? `<strong${fafcCls ? ` class="${fafcCls}"` : ""}>${num(fafcFails)}</strong>`
    : `<strong>${noDataHtml()}</strong>`;
  const imp = promOk ? (windowStats.impact || {}) : {};
  const req = promOk && !noData ? num(windowStats.totalRequests) : noDataHtml();
  const inv = promOk && !noData ? num(windowStats.totalInvalidations) : noDataHtml();
  const fadMs = Number(imp.avgFactoryDurationMs);
  const fad = promOk && (imp.factoryDurationCount || 0) > 0 && Number.isFinite(fadMs)
    ? `${Math.round(fadMs * 10) / 10} ms`
    : noDataHtml(dashTip);
  const timeSaved = promOk
    ? fmtDurationMs(imp.estFactoryTimeSavedMs)
    : noDataHtml(dashTip);
  const domN = promOk ? (windowStats.domains || []).length : 0;
  const epN = promOk ? (windowStats.endpoints || []).length : 0;
  const alerts = (o.alerts && o.alerts.length)
    ? `<span class="hm status-Degraded" title="${esc(o.alerts.join(" | "))}">⚠\u2009${o.alerts.length}</span>`
    : "";

  $("#headerMetrics").innerHTML = `
    <div class="hm-left">
      <span class="hm-cluster">
        <span class="hm" title="${esc(healthTitle)}">${healthDots}
          <strong class="${upClass}">${up}/${total || 0}</strong><span class="muted">\u2009up</span>
          ${down > 0 ? `<span class="status-Down">${fmtUnit(down, "down")}</span>` : ""}
          ${deg > 0 ? `<span class="status-Degraded">${fmtUnit(deg, "deg")}</span>` : ""}
        </span>
        <span data-metrics-pill-slot>${metricsStatusPillHtml(windowStats)}</span>
      </span>
      <span class="hm" title="${esc(METRIC_TITLES.req)}">Req <strong>${req}</strong></span>
      <span class="hm" title="${esc(METRIC_TITLES.inv)}">Inv <strong>${inv}</strong></span>
      <span class="hm" title="${esc(METRIC_TITLES.pipeline)}">${pipelineBar(pipe, false, { title: false })}</span>
      <span class="hm" title="${esc(METRIC_TITLES.outputCacheHitShare)}">OC hit % <strong>${oc}</strong></span>
      <span class="hm" title="${esc(METRIC_TITLES.dataCacheHitShare)}">DC hit % <strong>${fc}</strong></span>
      <span class="hm" title="${esc(METRIC_TITLES.factoryShare)}">FA run % <strong>${fac}</strong></span>
      <span class="hm" title="${esc(METRIC_TITLES.factoryFailures)}">FAFC ${fafc}</span>
      <span class="hm" title="${esc(METRIC_TITLES.avgFactoryDuration)}">FAD <strong>${fad}</strong></span>
      <span class="hm" title="${esc(METRIC_TITLES.staleShare)}">DC stale % <strong>${stale}</strong></span>
      <span class="hm" title="${esc(METRIC_TITLES.estTimeSaved)}">EFTS <strong>${timeSaved}</strong></span>
      ${alerts}
    </div>
    <span class="hm hm-entities muted" title="${esc(METRIC_TITLES.entities)}">${fmtUnit(domN, "dom")} ${fmtUnit(epN, "ep")}</span>
  `;
  refreshMetricsStatusPill();
}

export function scheduleRefresh() {
  if (headerTimer) {
    clearInterval(headerTimer);
    headerTimer = null;
  }
  if (pageTimer) {
    clearInterval(pageTimer);
    pageTimer = null;
  }
  const sec = liveChromeLock ? LIVE_REFRESH_SEC : getAutoRefreshSec();
  // When auto is on: soft background refresh (no full-page loading flash).
  // When off: still refresh header slowly so N/M up does not go stale forever.
  if (sec > 0) {
    pageTimer = setInterval(() => {
      refreshAll();
    }, sec * 1000);
  } else {
    headerTimer = setInterval(() => {
      refreshHeader({ silent: true });
    }, 30000);
  }
}

export function initRefreshControls() {
  const sel = $("#selAutoRefresh");
  if (sel) {
    sel.value = String(getAutoRefreshSec());
    sel.addEventListener("change", () => {
      if (liveChromeLock) return;
      setAutoRefreshSec(Number(sel.value) || 0);
    });
  }
  $("#btnHeaderRefresh")?.addEventListener("click", () => {
    refreshAll();
  });
}
