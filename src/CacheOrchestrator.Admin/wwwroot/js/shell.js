/**
 * Sticky chrome: cluster metrics header + Grafana-style auto-refresh.
 *
 * Layout (see index.html):
 * 1) brand / logo
 * 2) header metrics strip (`#headerMetrics`)
 * 3) menu strip
 */

import { api } from "./api.js";
import { $, main } from "./dom.js";
import { esc, fmtUnit, num, pct, pipelineBar } from "./format.js";
import { severityStack } from "./hints.js";

const REFRESH_KEY = "adminAutoRefreshSec";

let headerTimer = null;
let pageTimer = null;
/**
 * Last successful overview payload (optional debug / future use).
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

/** Lightweight header-only refresh (overview API). */
export async function refreshHeader() {
  try {
    const o = await api("/api/overview");
    setLastOverview(o);
    renderHeader(o);
    updateNavHintsBadge(o.hintSummary);
  } catch (err) {
    setLastOverview(null);
    $("#headerMetrics").innerHTML = `
      <span class="hm status-Down" title="${esc(err.message)}">Admin API error</span>
      <span class="hm muted">${esc(err.message)}</span>`;
    updateNavHintsBadge({ total: 0 });
  }
}

/** Header + full page re-route. */
export async function refreshAll() {
  await refreshHeader();
  if (routeHandler) await routeHandler();
}

/** Expose for empty-state buttons (`data-es-refresh`). */
window.__adminRefresh = refreshAll;

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
  const healthDots = [
    ...Array(o.healthyCount || 0).fill("ok"),
    ...Array(o.degradedCount || 0).fill("warn"),
    ...Array(o.downCount || 0).fill("bad"),
  ].map((c) => `<span class="dot ${c}"></span>`).join("") || `<span class="muted">—</span>`;

  const hs = o.hintSummary || { total: 0 };
  // Admin App probes each Local Admin GET /health → Healthy / Degraded / Down.
  const healthTitle = [
    `${up}/${total} healthy`,
    o.degradedCount ? `${o.degradedCount} degraded` : null,
    o.downCount ? `${o.downCount} down` : null,
  ].filter(Boolean).join(" · ");

  $("#headerMetrics").innerHTML = `
    <span class="hm" title="${esc(healthTitle)}">${healthDots}
      <strong>${up}/${total || 0}</strong><span class="muted">\u2009up</span>
      ${(o.downCount || 0) > 0 ? `<span class="status-Down">${fmtUnit(o.downCount, "down")}</span>` : ""}
      ${(o.degradedCount || 0) > 0 ? `<span class="status-Degraded">${fmtUnit(o.degradedCount, "deg")}</span>` : ""}
    </span>
    <span class="hm" title="Cluster recommendation urgency">${severityStack(hs)}</span>
    <span class="hm" title="Request pipeline (OC hit · FC hit · Origin · Bypass)">${pipelineBar(o.pipeline)}</span>
    <span class="hm" title="Output Cache hit share of requests">OC hit <strong>${pct(o.ocHitShare)}</strong></span>
    <span class="hm" title="Factory / origin share of requests">Origin <strong>${pct(o.originShare)}</strong></span>
    <span class="hm" title="Lifetime request count (sum)">Req <strong>${num(o.totalRequests)}</strong></span>
    <span class="hm" title="Lifetime invalidations (sum)">Inv <strong>${num(o.totalInvalidations)}</strong></span>
    <span class="hm muted" title="Domains / endpoints observed">${fmtUnit(o.domainCount, "dom")} · ${fmtUnit(o.endpointCount, "ep")}</span>
    ${(o.alerts && o.alerts.length) ? `<span class="hm status-Degraded" title="${esc(o.alerts.join(" | "))}">⚠\u2009${o.alerts.length}</span>` : ""}
  `;
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
  // When auto is on: full page refresh on interval.
  // When off: still refresh header slowly so N/M up does not go stale forever.
  if (sec > 0) {
    pageTimer = setInterval(() => {
      refreshAll();
    }, sec * 1000);
  } else {
    headerTimer = setInterval(refreshHeader, 30000);
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

export { main };
