/**
 * CacheOrchestrator Admin UI — SPA entry point.
 *
 * Soft refresh: fetch in background, repaint without "Loading…" flash (no SPA framework).
 * Static files are served by ASP.NET Core (`UseDefaultFiles` + `UseStaticFiles`).
 */

import { api } from "./api.js";
import {
  initRefreshControls,
  refreshHeader,
  scheduleRefresh,
  setRouteHandler,
} from "./shell.js";
import {
  mountTimeRangePicker,
  setMetricsCapability,
  subscribe as subscribeTimeRange,
} from "./time-range.js";
import { route } from "./views.js";

setRouteHandler(route);

window.addEventListener("hashchange", () => {
  route();
});

if (!location.hash) {
  location.hash = "#/overview";
}

/** Small version next to brand (from MinVer informational version). */
async function paintBrandVersion() {
  const el = document.getElementById("brandVersion");
  if (!el) return;
  try {
    const about = await api("/api/about");
    if (about?.version) el.textContent = `v${about.version}`;
  } catch {
    /* ignore — chrome still works */
  }
}

function onTimeRangeChange() {
  route({ soft: true });
  refreshHeader({ silent: true });
}

function initTimeRangeControl() {
  const host = document.getElementById("navTimeRangeHost");
  if (!host) return;
  host._trOnChange = onTimeRangeChange;
  mountTimeRangePicker(host, { onChange: onTimeRangeChange });
  subscribeTimeRange(() => {
    mountTimeRangePicker(host, { onChange: onTimeRangeChange });
    host._trOnChange = onTimeRangeChange;
  });
}

/** Probe Metrics store so windowed shortcuts enable/disable correctly. */
async function refreshMetricsCapability() {
  try {
    const status = await api("/api/metrics/status");
    const s = (status?.status || "").toLowerCase();
    if (s === "connected") setMetricsCapability("connected");
    else if (s === "disconnected") setMetricsCapability("disconnected");
    else if (s === "notconfigured" || s === "not_configured") setMetricsCapability("not_configured");
    else setMetricsCapability("unknown");
  } catch {
    setMetricsCapability("not_configured");
  }
}

initRefreshControls();
scheduleRefresh();
initTimeRangeControl();
paintBrandVersion();
refreshMetricsCapability();
// Single first paint path (overview fetch is deduped if header also loads).
route().then(() => {
  refreshHeader({ silent: true });
});
