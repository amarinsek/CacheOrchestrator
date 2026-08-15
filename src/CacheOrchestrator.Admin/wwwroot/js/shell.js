/**
 * Sticky chrome: cluster metrics header + Grafana-style auto-refresh.
 *
 * Layout (see index.html):
 * 1) brand / logo
 * 2) header metrics strip (`#headerMetrics`)
 * 3) menu strip
 *
 * Soft refresh (route({ soft: true })) repaints without a full "Loading…" flash.
 * Concurrent soft refreshes coalesce to the latest run.
 */

import { api } from "./api.js";
import { $, setRefreshing } from "./dom.js";
import { esc, fmtUnit, num, pct, pipelineBar } from "./format.js";
import { severityStack } from "./hints.js";

const REFRESH_KEY = "adminAutoRefreshSec";

let headerTimer = null;
let pageTimer = null;
/** Coalesce overlapping soft refreshes (auto-refresh + manual). */
let softRefreshBusy = false;
let softRefreshAgain = false;
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

export function getAutoRefreshSec() {
  const v = Number(localStorage.getItem(REFRESH_KEY) || "0");
  return [0, 5, 10, 30, 60, 300].includes(v) ? v : 0;
}

export function setAutoRefreshSec(sec) {
  localStorage.setItem(REFRESH_KEY, String(sec));
  scheduleRefresh();
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
    renderHeader(o);
    updateNavHintsBadge(o.hintSummary);
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

/** Non-blocking metrics store status for the chrome strip. */
async function refreshMetricsStatusPill() {
  try {
    const s = await api("/api/metrics/status");
    const el = $("#headerMetrics");
    if (!el) return;
    const existing = el.querySelector("[data-metrics-pill]");
    if (existing) existing.remove();
    if (s.status === "NotConfigured") return;
    const provider = s.provider || "Prometheus";
    const target = s.host ? `${provider} · ${s.host}` : provider;
    const cls = s.status === "Connected" ? "ok" : "bad";
    const title = s.status === "Connected"
      ? target
      : `${target} · not connected${s.error ? ` — ${s.error}` : ""}`;
    const label = s.status === "Connected" ? "metrics" : "metrics off";
    el.insertAdjacentHTML(
      "beforeend",
      `<span class="hm" data-metrics-pill title="${esc(title)}"><span class="dot ${cls}"></span><span class="muted">${esc(label)}</span></span>`);
  } catch {
    /* optional */
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
 * Always shows N/M up (healthy / configured).
 */
export function renderHeader(o) {
  const total = (o.instances || []).length
    || ((o.healthyCount || 0) + (o.degradedCount || 0) + (o.downCount || 0));
  const up = o.healthyCount ?? 0;
  const down = o.downCount ?? 0;
  const deg = o.degradedCount ?? 0;
  // Green only when all configured instances are healthy.
  const upClass = total === 0 || down > 0 || up < total
    ? "status-Down"
    : deg > 0
      ? "status-Degraded"
      : "status-Healthy";
  const healthDots = [
    ...Array(o.healthyCount || 0).fill("ok"),
    ...Array(o.degradedCount || 0).fill("warn"),
    ...Array(o.downCount || 0).fill("bad"),
  ].map((c) => `<span class="dot ${c}"></span>`).join("") || `<span class="muted">—</span>`;

  const hs = o.hintSummary || { total: 0 };
  const healthTitle = [
    `${up}/${total} healthy`,
    o.degradedCount ? `${o.degradedCount} degraded` : null,
    o.downCount ? `${o.downCount} down` : null,
  ].filter(Boolean).join(" · ");

  $("#headerMetrics").innerHTML = `
    <span class="hm" title="${esc(healthTitle)}">${healthDots}
      <strong class="${upClass}">${up}/${total || 0}</strong><span class="muted">\u2009up</span>
      ${down > 0 ? `<span class="status-Down">${fmtUnit(down, "down")}</span>` : ""}
      ${deg > 0 ? `<span class="status-Degraded">${fmtUnit(deg, "deg")}</span>` : ""}
    </span>
    <span class="hm" title="Cluster recommendation urgency">${severityStack(hs)}</span>
    <span class="hm" title="Request pipeline (OC hit · FC hit · Origin/factory · Bypass)">${pipelineBar(o.pipeline)}</span>
    <span class="hm" title="Output Cache hit share of requests">OC hit <strong>${pct(o.ocHitShare)}</strong></span>
    <span class="hm" title="Origin share = Fusion factory runs ÷ requests (CDN ‘origin’ = factory miss path)">Origin <strong>${pct(o.originShare)}</strong></span>
    <span class="hm" title="Lifetime request count (sum)">Req <strong>${num(o.totalRequests)}</strong></span>
    <span class="hm" title="Lifetime invalidations (sum)">Inv <strong>${num(o.totalInvalidations)}</strong></span>
    <span class="hm muted" title="Domains / endpoints observed">${fmtUnit(o.domainCount, "dom")} · ${fmtUnit(o.endpointCount, "ep")}</span>
    ${(o.alerts && o.alerts.length) ? `<span class="hm status-Degraded" title="${esc(o.alerts.join(" | "))}">⚠\u2009${o.alerts.length}</span>` : ""}
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
  const sec = getAutoRefreshSec();
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
      setAutoRefreshSec(Number(sel.value) || 0);
    });
  }
  $("#btnHeaderRefresh")?.addEventListener("click", () => {
    refreshAll();
  });
}
