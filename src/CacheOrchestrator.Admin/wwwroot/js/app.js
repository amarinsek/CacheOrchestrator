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
 *   shell.js        sticky header metrics + auto-refresh
 *   views.js        page renderers + route()
 *   app.js          bootstrap (this file)
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
refreshHeader();
route();
