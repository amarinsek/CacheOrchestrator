/**
 * Instances list + detail pages.
 */

import { api } from "./api.js";
import { $, beginPageLoad, main, mainHasContent, paintPage } from "./dom.js";
import {
  esc,
  formatLatencyMs,
  formatUptime,
  METRIC_TITLES,
  noDataHtml,
  num,
  tipAttr,
} from "./format.js";
import {
  applyButtonHtml,
  DOMAIN_SORT_OPTS,
  EP_SORT_OPTS,
  filterInstancesBySearch,
  INST_SORT_OPTS,
  inlineSortSelectHtml,
  sortDomains,
  sortEndpoints,
  sortInstances,
  sortSelectHtml,
} from "./filters.js";
import { severityStack } from "./hints.js";
import { navigate, setBreadcrumb } from "./router.js";
import * as shell from "./shell.js";
import {
  bindEmptyStateActions,
  bindEntityTableClicks,
  connectivityBanner,
  domainTableHtml,
  emptyStateHtml,
  endpointTableHtml,
  instanceTableHtml,
} from "./tables.js";
import { formatMetricsProvider, mountDetailMetrics } from "./views-metrics.js";
import {
  fetchWindowStatsIfNeeded,
  metricsRequiredEmpty,
  sliceWindowStatsForInstance,
  withWindowInstanceTraffic,
} from "./views-shared.js";

export async function renderInstancesList(params = new URLSearchParams(), opts = {}) {
  const soft = !!opts.soft;
  setBreadcrumb([]);
  const search = params.get("search") || "";
  const sort = params.get("sort") || "status";

  beginPageLoad(soft, `<div class="card"><p class="muted">Loading instances…</p></div>`);
  let overview;
  let windowStats = null;
  try {
    [overview, windowStats] = await Promise.all([
      api("/api/overview"),
      fetchWindowStatsIfNeeded(),
    ]);
  } catch (err) {
    if (soft && mainHasContent()) return;
    paintPage(`<div class="card">${emptyStateHtml("error", { detail: err.message })}</div>`, soft);
    bindEmptyStateActions(main());
    return;
  }
  await shell.refreshHeader({ silent: soft });
  const promOk = windowStats?.status === "Connected";
  const hintSum = promOk && windowStats.hintSummary ? windowStats.hintSummary : overview.hintSummary;
  shell.updateNavHintsBadge(hintSum);
  const list = sortInstances(
    filterInstancesBySearch(withWindowInstanceTraffic(overview.instances || [], windowStats), search),
    sort);

  // Metrics store status lives here (not a banner on every Metrics page load).
  let metricsCard = "";
  try {
    const ms = await api("/api/metrics/status");
    if (ms.status !== "NotConfigured") {
      const target = formatMetricsProvider(ms);
      const st = ms.status === "Connected"
        ? `<span class="badge ok">Connected</span>`
        : `<span class="badge warn">Disconnected</span>`;
      const lat = ms.latencyMs != null ? ` · probe ${Math.round(ms.latencyMs)} ms` : "";
      const err = ms.error ? ` — ${esc(ms.error)}` : "";
      metricsCard = `
        <div class="card" id="instMetricsStoreCard">
          <div class="card-head">
            <h2 title="Metrics - Metrics backend used for Live, tables, and charts">Metrics</h2>
          </div>
          <p style="margin:0">${st}
            ${esc(target)}${lat}${err}
          </p>
        </div>`;
    }
  } catch { /* optional */ }

  const bannerHtml = connectivityBanner(overview.instances || []);
  const tableHtml = instanceTableHtml(list);

  if (soft && $("#instRoot")) {
    const banner = $("#instBanner");
    if (banner) banner.innerHTML = bannerHtml;
    const head = $("#instHead");
    if (head) head.innerHTML = `Instances ${severityStack(hintSum)}`;
    const table = $("#instTable");
    if (table) table.innerHTML = tableHtml;
    const metricsHost = $("#instMetricsHost");
    if (metricsHost) metricsHost.innerHTML = metricsCard;
    bindEntityTableClicks($("#instTable") || main());
    bindEmptyStateActions(main());
    return;
  }

  paintPage(`
    <div id="instRoot">
    <div id="instBanner">${bannerHtml}</div>
    <div id="instMetricsHost">${metricsCard}</div>
    <div class="card">
      <h2 id="instHead">Instances ${severityStack(hintSum)}</h2>
      <form class="toolbar" id="instFilters">
        <label>Search<input name="search" type="search" value="${esc(search)}" placeholder="id or url" /></label>
        ${sortSelectHtml("sort", sort, INST_SORT_OPTS)}
        ${applyButtonHtml()}
      </form>
      <div id="instTable">${tableHtml}</div>
    </div>
    </div>`, soft);

  bindEntityTableClicks($("#instTable") || main());
  bindEmptyStateActions(main());

  $("#instFilters")?.addEventListener("submit", (ev) => {
    ev.preventDefault();
    const fd = new FormData(ev.target);
    navigate("instances", {
      search: fd.get("search"),
      sort: fd.get("sort"),
    });
  });
}

export async function renderInstanceDetail(id, params = new URLSearchParams(), opts = {}) {
  const soft = !!opts.soft;
  const domSort = params.get("domSort") || "requests";
  const epSort = params.get("epSort") || "requests";
  setBreadcrumb([
    { label: "Instances", href: "#/instances" },
    { label: id },
  ]);
  beginPageLoad(soft, `<p class="muted">Loading instance ${esc(id)}…</p>`);

  const [instances, windowStats] = await Promise.all([
    api("/api/instances"),
    fetchWindowStatsIfNeeded(),
  ]);
  const inst = instances.find((i) => i.id === id);
  const startedTitle = inst?.startedAtUtc
    ? new Date(inst.startedAtUtc).toISOString()
    : "";

  const st = (inst?.status === 0 || inst?.status === "Healthy") ? "Healthy"
    : (inst?.status === 1 || inst?.status === "Degraded") ? "Degraded"
    : (inst?.status === 2 || inst?.status === "Down") ? "Down"
    : (inst?.status || "unknown");

  const promOk = windowStats?.status === "Connected";
  const stats = sliceWindowStatsForInstance(
    windowStats,
    id,
    inst?.reportedInstanceId);
  const domainsSorted = sortDomains(stats.domains || [], domSort);
  const endpointsSorted = sortEndpoints(stats.endpoints || [], epSort).slice(0, 50);

  if (soft && $("#instMetricsMount")?.dataset?.metricsReady === "1") {
    const head = $("#instDetailHead");
    if (head) head.innerHTML = instanceDetailHeadHtml(id, inst, stats, st, startedTitle, promOk);
    const domHost = $("#instDomTable");
    if (domHost) {
      domHost.innerHTML = promOk
        ? domainTableHtml(domainsSorted)
        : metricsRequiredEmpty();
    }
    const epHost = $("#instEpTable");
    if (epHost) {
      epHost.innerHTML = promOk
        ? endpointTableHtml(endpointsSorted)
        : metricsRequiredEmpty();
    }
    bindEntityTableClicks(main());
    mountDetailMetrics("instMetricsMount", { scope: "instance", instanceId: id });
    return;
  }

  paintPage(`
    <div id="instDetailHead">${instanceDetailHeadHtml(id, inst, stats, st, startedTitle, promOk)}</div>
    <div class="card">
      <div class="card-head">
        <h2>Domains on instance</h2>
        ${promOk ? inlineSortSelectHtml("instDomSort", domSort, DOMAIN_SORT_OPTS) : ""}
      </div>
      <div id="instDomTable">${promOk ? domainTableHtml(domainsSorted) : metricsRequiredEmpty()}</div>
    </div>
    <div class="card">
      <div class="card-head">
        <h2>Endpoints on instance</h2>
        ${promOk ? inlineSortSelectHtml("instEpSort", epSort, EP_SORT_OPTS) : ""}
      </div>
      <div id="instEpTable">${promOk ? endpointTableHtml(endpointsSorted) : metricsRequiredEmpty()}</div>
    </div>
    <div id="instMetricsMount"></div>
    <p><a href="#/instances">← Instances</a>
      · <a href="#/operations">Operations</a></p>`, soft);

  bindEntityTableClicks(main());
  $("#instDomSort")?.addEventListener("change", (ev) => {
    navigate("instances", { id, domSort: ev.target.value, epSort });
  });
  $("#instEpSort")?.addEventListener("change", (ev) => {
    navigate("instances", { id, domSort, epSort: ev.target.value });
  });
  mountDetailMetrics("instMetricsMount", { scope: "instance", instanceId: id });
}

export function instanceDetailHeadHtml(id, inst, stats, st, startedTitle, promOk = false) {
  const startedDisp = startedTitle
    ? startedTitle.replace("T", " ").replace(/\.\d+Z$/, "Z")
    : "—";
  const reqFromDomains = (stats.domains || []).reduce((s, d) => s + (d.requests || 0), 0);
  const errLine = inst?.error
    ? `<p class="muted status-Down" style="margin:0.5rem 0 0">${esc(inst.error)}</p>`
    : "";
  return `
    <div class="card">
      <h2>Instance <code>${esc(id)}</code>
        ${severityStack(inst?.hintSummary)}
      </h2>
      ${errLine}
      <div class="kpi-row">
        <div class="kpi"${tipAttr("status")}>
          <div class="label">Status</div>
          <div class="value status-${esc(st)}" style="font-size:1.05rem">${esc(st)}</div>
        </div>
        <div class="kpi" title="${esc(METRIC_TITLES.uptime)}${startedTitle ? ` (${startedTitle})` : ""}"><div class="label">Uptime</div><div class="value col-uptime" style="font-size:1.05rem">${esc(formatUptime(inst?.uptimeSeconds))}</div></div>
        <div class="kpi" title="Started (UTC) - Process start time"><div class="label">Started (UTC)</div><div class="value" style="font-size:0.85rem">${esc(startedDisp)}</div></div>
        <div class="kpi"${tipAttr("latency")}><div class="label">Latency</div><div class="value" style="font-size:1.05rem">${formatLatencyMs(inst?.latencyMs)}</div></div>
        <div class="kpi"${tipAttr("req")}><div class="label">Req</div><div class="value">${promOk ? num(reqFromDomains) : noDataHtml()}</div></div>
      </div>
    </div>`;
}
