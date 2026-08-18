/**
 * Overview page — cluster health + Prometheus window traffic.
 */

import { api } from "./api.js";
import { $, beginPageLoad, main, mainHasContent, paintPage } from "./dom.js";
import {
  esc,
  fmtDurationMs,
  METRIC_TITLES,
  noDataHtml,
  num,
  pct,
  pipelineBar,
  tipAttr,
} from "./format.js";
import {
  DOMAIN_SORT_OPTS,
  EP_SORT_OPTS,
  INST_SORT_OPTS,
  inlineSortSelectHtml,
  sortDomains,
  sortEndpoints,
  sortInstances,
} from "./filters.js";
import { severityStack } from "./hints.js";
import { navigate, setBreadcrumb } from "./router.js";
import * as shell from "./shell.js";
import {
  allInstancesDown,
  bindEmptyStateActions,
  bindEntityTableClicks,
  connectivityBanner,
  domainTableHtml,
  emptyStateHtml,
  endpointTableHtml,
  instanceTableHtml,
  noInstancesConfigured,
} from "./tables.js";
import { metricsOverviewSectionHtml } from "./views-metrics.js";
import {
  bindGotoHints,
  fetchWindowStatsIfNeeded,
  instancesUpClass,
  withWindowInstanceTraffic,
} from "./views-shared.js";

export async function renderOverview(params = new URLSearchParams(), opts = {}) {
  const soft = !!opts.soft;
  setBreadcrumb([]);

  // Prefer last overview for instant paint (header may already have fetched it).
  const cached = shell.getLastOverview();
  if (cached && (!soft || !mainHasContent())) {
    await paintOverviewBody(cached, params, soft);
  } else if (!soft && !mainHasContent()) {
    beginPageLoad(false, `<div class="card"><p class="muted">Loading overview…</p></div>`);
  }

  let o;
  try {
    o = await api("/api/overview");
  } catch (err) {
    if (soft && mainHasContent()) return; // keep previous page on soft failure
    if (cached) return; // keep cached paint
    paintPage(`<div class="card">${emptyStateHtml("error", {
      title: "Cannot load overview",
      detail: err.message,
    })}</div>`, soft);
    bindEmptyStateActions(main());
    return;
  }
  shell.setLastOverview(o);
  // Header traffic KPIs come from Prometheus; health from overview.
  await shell.refreshHeader({ silent: soft });
  await paintOverviewBody(o, params, soft);
}

export async function paintOverviewBody(o, params, soft) {
  // All traffic from Prometheus only. Overview API = instance health.
  const windowStats = await fetchWindowStatsIfNeeded();
  const promOk = windowStats && windowStats.status === "Connected";

  const offline = allInstancesDown(o.instances);
  const noCfg = noInstancesConfigured(o.instances);
  const instSort = params.get("instSort") || "status";
  const domSort = params.get("domSort") || "factoryShare";
  const epSort = params.get("epSort") || "factoryShare";
  const instancesSorted = sortInstances(
    withWindowInstanceTraffic(o.instances || [], windowStats),
    instSort);
  // Version overlay from domain config API (current values).
  let verByName = {};
  try {
    const cfgFan = await api("/api/domains");
    verByName = Object.fromEntries((cfgFan.data || []).map((d) => [d.name, d.version]));
  } catch { /* optional */ }
  const srcDomains = promOk ? (windowStats.domains || []) : [];
  const srcEndpoints = promOk ? (windowStats.endpoints || []) : [];
  const domainsForTable = srcDomains.map((d) =>
    (d.version == null || d.version === "") && verByName[d.name]
      ? { ...d, version: verByName[d.name] }
      : d);
  const top5Domains = sortDomains(domainsForTable, domSort).slice(0, 5);
  const top5Endpoints = sortEndpoints(srcEndpoints, epSort).slice(0, 5);
  const windowed = promOk;

  const offlineDetail =
    "Start target apps with Cache:Admin:Enabled and matching ApiKey, then refresh.";

  const tableKind = noCfg ? "config" : offline ? "offline" : "domains";
  const epKind = noCfg ? "config" : offline ? "offline" : "endpoints";

  // Soft refresh: patch live regions only so charts / tables do not remount every interval.
  if (soft && $("#ovRoot")) {
    const bannerHost = $("#ovBannerHost");
    if (bannerHost) bannerHost.innerHTML = connectivityBanner(o.instances);
    const kpis = $("#ovKpis");
    if (kpis) kpis.innerHTML = overviewKpiHtml(o, windowStats);
    const pipe = $("#ovPipeline");
    if (pipe) pipe.innerHTML = pipelineBar(windowed ? windowStats.pipeline : o.pipeline, true);
    const alerts = $("#ovAlerts");
    if (alerts) {
      alerts.innerHTML = o.alerts?.length
        ? `<div class="card"><h2>Alerts</h2><ul class="alert-list">${o.alerts.map((a) => `<li>${esc(a)}</li>`).join("")}</ul></div>`
        : "";
    }
    const instTable = $("#ovInstTable");
    if (instTable) instTable.innerHTML = instanceTableHtml(instancesSorted, { kind: "config" });
    const domTable = $("#ovDomTable");
    if (domTable) {
      domTable.innerHTML = domainTableHtml(top5Domains, {
        kind: tableKind,
        title: offline ? "No domain data — apps offline" : undefined,
        detail: offline ? offlineDetail : undefined,
      });
    }
    const epTable = $("#ovEpTable");
    if (epTable) {
      epTable.innerHTML = endpointTableHtml(top5Endpoints, {
        kind: epKind,
        title: offline ? "No endpoint data — apps offline" : undefined,
        detail: offline ? offlineDetail : undefined,
      });
    }
    bindEntityTableClicks(main());
    bindEmptyStateActions(main());
    bindGotoHints(main());
    const mount = $("#ovMetricsMount");
    metricsOverviewSectionHtml({ soft: true, mountEl: mount }).then((html) => {
      const m = $("#ovMetricsMount");
      if (!m) return;
      if (html === null) {
        m.dataset.ready = "1";
        return;
      }
      if (html) {
        m.innerHTML = html;
        m.dataset.ready = "1";
      }
    });
    return;
  }

  paintPage(`
    <div id="ovRoot">
    <div id="ovBannerHost">${connectivityBanner(o.instances)}</div>
    <div class="kpi-row" id="ovKpis">${overviewKpiHtml(o, windowStats)}</div>
    <div class="card">
      <h2>Cluster pipeline</h2>
      <div id="ovPipeline">${pipelineBar(windowed ? windowStats.pipeline : o.pipeline, true)}</div>
      <p class="muted" style="margin:0.5rem 0 0;font-size:0.85rem" title="${esc(METRIC_TITLES.pipeline)}">OC hit share · FC hit share · Factory share · Bypass — shares of total requests</p>
    </div>
    <div id="ovAlerts">${o.alerts?.length ? `<div class="card"><h2>Alerts</h2><ul class="alert-list">${o.alerts.map((a) => `<li>${esc(a)}</li>`).join("")}</ul></div>` : ""}</div>
    <div class="card">
      <div class="card-head">
        <h2>Instances</h2>
        ${inlineSortSelectHtml("ovInstSort", instSort, INST_SORT_OPTS)}
      </div>
      <div id="ovInstTable">
        ${instanceTableHtml(instancesSorted, { kind: "config" })}
      </div>
    </div>
    <div class="card">
      <div class="card-head">
        <h2>Domains <span class="badge">top 5</span></h2>
        ${inlineSortSelectHtml("ovDomSort", domSort, DOMAIN_SORT_OPTS)}
      </div>
      <div id="ovDomTable">
        ${domainTableHtml(top5Domains, {
          kind: tableKind,
          title: offline ? "No domain data — apps offline" : undefined,
          detail: offline ? offlineDetail : undefined,
        })}
      </div>
      ${!offline && promOk && domainsForTable.length ? `<p style="margin:0.75rem 0 0"><a href="#/domains">All domains →</a></p>` : ""}
    </div>
    <div class="card">
      <div class="card-head">
        <h2>Endpoints <span class="badge">top 5</span></h2>
        ${inlineSortSelectHtml("ovEpSort", epSort, EP_SORT_OPTS)}
      </div>
      <div id="ovEpTable">
        ${endpointTableHtml(top5Endpoints, {
          kind: epKind,
          title: offline ? "No endpoint data — apps offline" : undefined,
          detail: offline ? offlineDetail : undefined,
        })}
      </div>
      ${!offline && promOk && srcEndpoints.length ? `<p style="margin:0.75rem 0 0"><a href="#/endpoints">All endpoints →</a></p>` : ""}
    </div>
    <div id="ovMetricsMount"><p class="muted small">Loading charts…</p></div>
    </div>`, soft);

  bindEntityTableClicks(main());
  bindEmptyStateActions(main());
  bindGotoHints(main());

  const ovSortParams = (patch) => ({
    instSort,
    domSort,
    epSort,
    ...patch,
  });
  $("#ovInstSort")?.addEventListener("change", (ev) => {
    navigate("overview", ovSortParams({ instSort: ev.target.value }));
  });
  $("#ovDomSort")?.addEventListener("change", (ev) => {
    navigate("overview", ovSortParams({ domSort: ev.target.value }));
  });
  $("#ovEpSort")?.addEventListener("change", (ev) => {
    navigate("overview", ovSortParams({ epSort: ev.target.value }));
  });

  const mount = $("#ovMetricsMount");
  metricsOverviewSectionHtml({ soft: false, mountEl: mount }).then((html) => {
    const m = $("#ovMetricsMount");
    if (!m) return;
    if (html) {
      m.innerHTML = html;
      m.dataset.ready = "1";
    } else {
      m.innerHTML = "";
      delete m.dataset.ready;
    }
  });
}

/**
 * @param {object} o overview DTO (instances / health always Admin)
 * @param {object|null} [windowStats] /api/stats/window when Range is windowed + Prom connected
 */
export function overviewKpiHtml(o, windowStats = null) {
  const promOk = windowStats && windowStats.status === "Connected";
  const noData = promOk && windowStats.noData;
  const scopeTip = promOk ? "Selected time range" : "Connect metrics to see traffic KPIs";

  const imp = promOk ? (windowStats.impact || {}) : {};
  const oc = !promOk || noData || windowStats.ocHitShare == null
    ? noDataHtml(promOk ? "No samples in this time range" : "Metrics offline")
    : pct(windowStats.ocHitShare);
  const fc = !promOk || noData || windowStats.fcHitShare == null
    ? noDataHtml(promOk ? "No samples in this time range" : "Metrics offline")
    : pct(windowStats.fcHitShare);
  const fac = !promOk || noData || windowStats.factoryShare == null
    ? noDataHtml(promOk ? "No samples in this time range" : "Metrics offline")
    : pct(windowStats.factoryShare);

  return `
      <div class="kpi"><div class="label">Instances up</div><div class="value ${instancesUpClass(o)}">${o.healthyCount} / ${(o.instances || []).length}</div></div>
      <div class="kpi" title="${esc(scopeTip)}"><div class="label">Requests</div><div class="value">${promOk && !noData ? num(windowStats.totalRequests) : noDataHtml()}</div></div>
      <div class="kpi" title="${esc(scopeTip)}"><div class="label">Invalidations</div><div class="value">${promOk && !noData ? num(windowStats.totalInvalidations) : noDataHtml()}</div></div>
      <div class="kpi"${tipAttr("ocHitShare")} title="${esc(scopeTip)}"><div class="label">OC hit %</div><div class="value">${oc}</div></div>
      <div class="kpi"${tipAttr("fcHitShare")} title="${esc(scopeTip)}"><div class="label">FC hit %</div><div class="value">${fc}</div></div>
      <div class="kpi"${tipAttr("factoryShare")} title="${esc(scopeTip)}"><div class="label">Factory %</div><div class="value">${fac}</div></div>
      <div class="kpi"${tipAttr("estTimeSaved")}><div class="label">Time saved</div><div class="value" style="font-size:1rem">${promOk ? fmtDurationMs(imp.estFactoryTimeSavedMs) : noDataHtml()}</div></div>
      <div class="kpi kpi-hints" role="link" tabindex="0" data-goto-hints="1" title="Open Hints"><div class="label">Cluster hints</div><div class="value">${severityStack(promOk ? windowStats.hintSummary : o.hintSummary)}</div></div>`;
}
