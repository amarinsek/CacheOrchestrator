/**
 * Route dispatch for the Admin SPA.
 * Page renderers live in focused `views-*.js` modules.
 */

import { main, mainHasContent } from "./dom.js";
import { navigate, parseHash, setNavActive } from "./router.js";
import * as shell from "./shell.js";
import { bindEmptyStateActions, emptyStateHtml } from "./tables.js";
import { renderDomainsList, renderDomainDetail } from "./views-domains.js";
import { renderEndpointsList, renderEndpointDetail } from "./views-endpoints.js";
import { renderHintsPage } from "./views-hints.js";
import { renderInstancesList, renderInstanceDetail } from "./views-instances.js";
import { renderLive } from "./views-live.js";
import { renderMetrics } from "./views-metrics.js";
import { renderOperations } from "./views-operations.js";
import { renderOverview } from "./views-overview.js";
import { renderSettingsPage } from "./views-settings.js";

export { renderOverview } from "./views-overview.js";
export { renderEndpointsList, renderEndpointDetail } from "./views-endpoints.js";
export { renderDomainsList, renderDomainDetail } from "./views-domains.js";
export { renderInstancesList, renderInstanceDetail } from "./views-instances.js";
export { renderHintsPage } from "./views-hints.js";
export { renderOperations } from "./views-operations.js";
export { renderSettingsPage } from "./views-settings.js";

/**
 * Map current hash to a view.
 * @param {{ soft?: boolean }} [opts] soft: background refresh without Loading flash
 */
export async function route(opts = {}) {
  const soft = !!opts.soft;
  const { path, params } = parseHash();
  const root = path.split("/")[0] || "overview";
  setNavActive(root);
  // Live uses a fixed 1m lookback + 5s refresh — lock Range / interval pickers.
  shell.setLiveChromeMode(root === "live");

  // Soft refresh: keep Operations form intact (mutations in progress).
  if (soft && root === "operations") {
    await shell.refreshHeader({ silent: true });
    return;
  }

  // Soft non-overview pages: refresh chrome header in parallel with page data.
  const headerTask = soft && root !== "overview"
    ? shell.refreshHeader({ silent: true })
    : Promise.resolve();

  try {
    await Promise.all([
      headerTask,
      (async () => {
        if (root === "overview" || path === "") {
          await renderOverview(params, { soft });
        } else if (root === "endpoints") {
          const routeName = params.get("route");
          if (routeName) await renderEndpointDetail(routeName, { soft });
          else await renderEndpointsList(params, { soft });
        } else if (root === "domains") {
          const name = params.get("name");
          if (name) await renderDomainDetail(name, params, { soft });
          else await renderDomainsList(params, { soft });
        } else if (root === "instances") {
          const id = params.get("id");
          if (id) await renderInstanceDetail(id, params, { soft });
          else await renderInstancesList(params, { soft });
        } else if (root === "operations") {
          await renderOperations(params);
        } else if (root === "hints") {
          await renderHintsPage(params, { soft });
        } else if (root === "metrics") {
          await renderMetrics(params, { soft });
        } else if (root === "live") {
          await renderLive(params, { soft });
        } else if (root === "settings") {
          if (!soft) await renderSettingsPage();
        } else if (!soft) {
          navigate("overview");
        }
      })(),
    ]);
  } catch (err) {
    // Browser console only — Admin Console App process logs do not capture SPA errors.
    console.error("[Admin UI] route failed", err);
    if (soft && mainHasContent()) return;
    main().innerHTML = `<div class="card">${emptyStateHtml("error", {
      title: "Page failed to load",
      detail: err?.message || String(err),
    })}</div>`;
    bindEmptyStateActions(main());
  }
}
