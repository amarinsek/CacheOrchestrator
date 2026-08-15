/**
 * CacheOrchestrator Admin UI — SPA entry point.
 *
 * Module layout (ES modules, no bundler):
 *   dom.js          query helpers
 *   api.js          /api/* client
 *   format.js       numbers, units, pipeline bar
 *   hints.js        recommendation badges / lists / collect
 *   filters.js      multi-select, sort, search
 *   tables.js       entity tables + empty states
 *   router.js       hash routing helpers
 *   shell.js        sticky header metrics + soft auto-refresh
 *   charts.js       SVG line charts for Metrics
 *   views-metrics.js Metrics page + overview embed
 *   views.js        page renderers + route({ soft })
 *   app.js          bootstrap (this file)
 *
 * Soft refresh: fetch in background, repaint without "Loading…" flash (no SPA framework).
 *
 * Static files are served by ASP.NET Core (`UseDefaultFiles` + `UseStaticFiles`).
 */

import {
  initRefreshControls,
  refreshHeader,
  scheduleRefresh,
  setRouteHandler,
} from "./shell.js";
import { route } from "./views.js";

setRouteHandler(route);

window.addEventListener("hashchange", () => {
  route();
});

if (!location.hash) {
  location.hash = "#/overview";
}

initRefreshControls();
scheduleRefresh();
// Single first paint path (overview fetch is deduped if header also loads).
route().then(() => {
  // Ensure chrome strip is filled even if overview paint path skipped header.
  refreshHeader({ silent: true });
});
