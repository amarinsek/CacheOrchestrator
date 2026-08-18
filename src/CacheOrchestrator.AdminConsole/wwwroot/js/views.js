/**
 * Page views (hash routes) for the Admin SPA.
 *
 * Each `render*` function owns its own data load + HTML + event bindings.
 * Shared tables / filters / hints live in sibling modules.
 */

import { api } from "./api.js";
import { $, beginPageLoad, main, mainHasContent, paintPage } from "./dom.js";
import {
  currentValueHtml,
  esc,
  formatLatencyMs,
  formatUptime,
  factoryShareOf,
  fmtDurationMs,
  fmtUnit,
  impactBandLabel,
  METRIC_TITLES,
  noDataHtml,
  num,
  pct,
  pipelineBar,
  thMetric,
  tipAttr,
} from "./format.js";
import {
  appendMetricsRangeParams,
  setMetricsCapability,
} from "./time-range.js";
import {
  applyButtonHtml,
  bindMultiSelects,
  csvParamFromSelection,
  DOMAIN_SORT_OPTS,
  EP_SORT_OPTS,
  filterDomainsBySearch,
  filterInstancesBySearch,
  INST_SORT_OPTS,
  inlineSortSelectHtml,
  multiSelectHtml,
  parseCsvParam,
  readMultiSelect,
  sortDomains,
  sortEndpoints,
  sortInstances,
  sortSelectHtml,
} from "./filters.js";
import { renderLive } from "./views-live.js";
import {
  collectHintRows,
  hintBadges,
  recommendationsSectionHtml,
  severityStack,
  summarizeHints,
} from "./hints.js";
import { navigate, parseHash, setBreadcrumb, setNavActive } from "./router.js";
import * as shell from "./shell.js";
import {
  allInstancesDown,
  bindEmptyStateActions,
  bindEntityTableClicks,
  connectivityBanner,
  domainTableHtml,
  emptyStateHtml,
  endpointTableHtml,
  impactDetailHtml,
  impactKpiRowHtml,
  instanceTableHtml,
  layerDetailFc,
  layerDetailOc,
  noInstancesConfigured,
} from "./tables.js";
import {
  formatMetricsProvider,
  metricsOverviewSectionHtml,
  mountDetailMetrics,
  renderMetrics,
} from "./views-metrics.js";

// —— Overview ——

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

async function paintOverviewBody(o, params, soft) {
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

function bindGotoHints(root) {
  (root || document).querySelectorAll("[data-goto-hints]").forEach((el) => {
    if (el.dataset.boundHints === "1") return;
    el.dataset.boundHints = "1";
    const go = (ev) => {
      ev.preventDefault();
      navigate("hints");
    };
    el.addEventListener("click", go);
    el.addEventListener("keydown", (ev) => {
      if (ev.key === "Enter" || ev.key === " ") go(ev);
    });
  });
}

/**
 * @param {object} o overview DTO (instances / health always Admin)
 * @param {object|null} [windowStats] /api/stats/window when Range is windowed + Prom connected
 */
function overviewKpiHtml(o, windowStats = null) {
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

/**
 * Prometheus window stats (only source of traffic counters).
 * Returns null when Metrics store is not usable.
 */
async function fetchWindowStatsIfNeeded(domainsCsv) {
  try {
    const q = appendMetricsRangeParams(new URLSearchParams());
    if (domainsCsv) q.set("domains", domainsCsv);
    const w = await api("/api/stats/window?" + q.toString());
    if (w?.status === "Connected") {
      setMetricsCapability("connected");
      return w;
    }
    if (w?.status === "Disconnected") setMetricsCapability("disconnected");
    else if (w?.status === "NotConfigured") setMetricsCapability("not_configured");
    return w;
  } catch {
    setMetricsCapability("disconnected");
    return null;
  }
}

/**
 * Attach Prometheus window request totals to instance health rows.
 * Matches Console instance id or reportedInstanceId to scrape label instance_id.
 * @returns {Array} instances with requests set (number) or null when metrics offline
 */
function withWindowInstanceTraffic(instances, windowStats) {
  const list = instances || [];
  const promOk = windowStats?.status === "Connected";
  if (!promOk) {
    return list.map((i) => ({ ...i, requests: null }));
  }
  /** @type {Record<string, number>} */
  const map = {};
  /** @type {Set<string>} */
  const fromDomain = new Set();
  const add = (id, n) => {
    if (!id) return;
    map[id] = (map[id] || 0) + (n || 0);
  };
  for (const d of windowStats.domains || []) {
    for (const bi of d.byInstance || []) {
      if (!bi.instanceId) continue;
      add(bi.instanceId, bi.requests);
      fromDomain.add(bi.instanceId);
    }
    if (d.instanceId) {
      add(d.instanceId, d.requests);
      fromDomain.add(d.instanceId);
    }
  }
  // Endpoints only for instances that had no domain by-instance rows (avoid double-count).
  for (const e of windowStats.endpoints || []) {
    for (const bi of e.byInstance || []) {
      if (bi.instanceId && !fromDomain.has(bi.instanceId)) {
        add(bi.instanceId, bi.requests);
      }
    }
  }
  return list.map((i) => {
    let req = map[i.id];
    if (req == null && i.reportedInstanceId) req = map[i.reportedInstanceId];
    return { ...i, requests: req ?? 0 };
  });
}

/**
 * Rows for a single instance from window stats (domains / endpoints).
 * Matches Console id or reported scrape id against byInstance.instanceId.
 */
function sliceWindowStatsForInstance(windowStats, instanceId, reportedId) {
  const ids = new Set([instanceId, reportedId].filter(Boolean));
  const match = (id) => id && ids.has(id);
  const domainsOnInst = [];
  const endpointsOnInst = [];
  if (windowStats?.status !== "Connected") {
    return { domains: domainsOnInst, endpoints: endpointsOnInst };
  }
  for (const d of windowStats.domains || []) {
    const bi = (d.byInstance || []).find((x) => match(x.instanceId));
    if (bi) domainsOnInst.push({ ...bi, name: d.name, version: bi.version || d.version });
    else if (match(d.instanceId)) domainsOnInst.push(d);
  }
  for (const e of windowStats.endpoints || []) {
    const bi = (e.byInstance || []).find((x) => match(x.instanceId));
    if (bi) {
      endpointsOnInst.push({
        ...bi,
        route: e.route || bi.route,
        configuredDomain: e.configuredDomain || bi.configuredDomain,
      });
    } else if (match(e.instanceId)) {
      endpointsOnInst.push(e);
    }
  }
  return { domains: domainsOnInst, endpoints: endpointsOnInst };
}

function metricsRequiredEmpty(detail) {
  return emptyStateHtml("metrics-config", {
    title: "Metrics not connected",
    detail: detail
      || "Set AdminConsole:Metrics (Enabled, Provider, BaseUrl) to enable statistics.",
    actions: [
      { label: "Refresh", onclick: "window.__adminRefresh && window.__adminRefresh()" },
      { label: "Instances", href: "#/instances" },
    ],
  });
}

// —— Endpoints ——

export async function renderEndpointsList(params, opts = {}) {
  const soft = !!opts.soft;
  setBreadcrumb([]);
  const search = params.get("search") || "";
  const sort = params.get("sort") || "requests";
  const take = params.get("take") || "50";
  const skip = Number(params.get("skip") || "0");
  const selDomains = parseCsvParam(params, "domains");

  beginPageLoad(soft, `<div class="card"><p class="muted">Loading endpoints…</p></div>`);

  let instanceList = [];
  try {
    instanceList = await api("/api/instances");
  } catch (err) {
    if (soft && mainHasContent()) return;
    paintPage(`<div class="card">${emptyStateHtml("error", { detail: err.message })}</div>`, soft);
    bindEmptyStateActions(main());
    return;
  }

  let domainOpts = [];
  let list = [];
  let loadError = null;
  let promOk = false;
  if (!(selDomains !== null && selDomains.length === 0)) {
    try {
      const domainsCsv = selDomains?.length ? selDomains.join(",") : undefined;
      const w = await fetchWindowStatsIfNeeded(domainsCsv);
      if (w?.status === "Connected") {
        promOk = true;
        domainOpts = (w.domains || []).map((d) => ({ id: d.name, label: d.name }));
        list = w.endpoints || [];
        if (search) {
          const q = search.toLowerCase();
          list = list.filter((e) =>
            (e.route || "").toLowerCase().includes(q)
            || (e.configuredDomain || "").toLowerCase().includes(q));
        }
        list = sortEndpoints(list, sort);
        const takeN = Number(take) || 50;
        list = list.slice(skip, skip + takeN);
      } else {
        loadError = w?.error || "Metrics not connected.";
      }
    } catch (err) {
      loadError = err.message;
    }
  }

  const emptyCtx = promOk
    ? {
      kind: loadError ? "error" : "endpoints",
      title: loadError ? "Failed to load endpoints" : undefined,
      detail: loadError || "No endpoint traffic in this time range (route labels must be enabled on apps).",
    }
    : {
      kind: "metrics-config",
      title: "Metrics not connected",
      detail: loadError || "Connect metrics to see endpoints.",
    };

  const tableInner = promOk
    ? `${endpointTableHtml(list, emptyCtx)}
        ${list.length ? `
        <div class="pager">
          <button type="button" class="secondary" id="epPrev" ${skip <= 0 ? "disabled" : ""}>Prev</button>
          <span>skip ${skip} · ${list.length} rows</span>
          <button type="button" class="secondary" id="epNext" ${list.length < Number(take) ? "disabled" : ""}>Next</button>
        </div>` : ""}`
    : metricsRequiredEmpty(loadError);

  const pageParams = () => ({
    search,
    sort,
    take,
    domains: csvParamFromSelection(selDomains),
  });

  const bindEpPager = () => {
    $("#epPrev")?.addEventListener("click", () => navigate("endpoints", {
      ...pageParams(),
      skip: Math.max(0, skip - Number(take)),
    }));
    $("#epNext")?.addEventListener("click", () => navigate("endpoints", {
      ...pageParams(),
      skip: skip + Number(take),
    }));
  };

  // Soft refresh: keep filter focus; only patch banner + table.
  if (soft && $("#epRoot")) {
    const banner = $("#epBanner");
    if (banner) banner.innerHTML = connectivityBanner(instanceList);
    const table = $("#epTable");
    if (table) table.innerHTML = tableInner;
    bindEmptyStateActions(main());
    bindEntityTableClicks($("#epTable") || main());
    bindEpPager();
    return;
  }

  paintPage(`
    <div id="epRoot">
    <div id="epBanner">${connectivityBanner(instanceList)}</div>
    <div class="card">
      <h2>Endpoints <span class="badge">primary unit</span></h2>
      ${promOk ? `
      <form class="toolbar" id="epFilters">
        <label>Search<input name="search" type="search" value="${esc(search)}" placeholder="route or domain" /></label>
        ${multiSelectHtml("epDom", "Domains", domainOpts, selDomains)}
        ${sortSelectHtml("sort", sort, EP_SORT_OPTS)}
        ${applyButtonHtml()}
      </form>
      <div id="epTable">${tableInner}</div>`
    : `<div id="epTable">${tableInner}</div>`}
    </div>
    </div>`, soft);

  bindEmptyStateActions(main());
  bindEntityTableClicks($("#epTable") || main());

  const form = $("#epFilters");
  if (form) {
    bindMultiSelects(form);
    form.addEventListener("submit", (ev) => {
      ev.preventDefault();
      const fd = new FormData(form);
      navigate("endpoints", {
        search: fd.get("search"),
        domains: csvParamFromSelection(readMultiSelect(form, "epDom")),
        sort: fd.get("sort"),
        take,
        skip: 0,
      });
    });
  }
  bindEpPager();
}

export async function renderEndpointDetail(routeName, opts = {}) {
  const soft = !!opts.soft;
  setBreadcrumb([
    { label: "Endpoints", href: "#/endpoints" },
    { label: routeName },
  ]);
  beginPageLoad(soft, `<p class="muted">Loading ${esc(routeName)}…</p>`);

  const w = await fetchWindowStatsIfNeeded();
  let ep = (w?.status === "Connected" ? w.endpoints : [])?.find((e) => e.route === routeName);
  if (!ep) {
    if (!(soft && mainHasContent())) {
      paintPage(`<div class="card"><p class="status-Down">Endpoint not found: <code>${esc(routeName)}</code></p>
      <a href="#/endpoints">← Back</a></div>`, soft);
    }
    return;
  }
  // Soft: only refresh counters above metrics — keep chart DOM alive.
  if (soft && $("#epMetricsMount")?.dataset?.metricsReady === "1") {
    const head = $("#epDetailHead");
    if (head) head.innerHTML = endpointDetailHeadHtml(ep);
    main().querySelectorAll("tr.clickable[data-id]").forEach((tr) => {
      tr.addEventListener("click", () => navigate("instances", { id: tr.dataset.id }));
    });
    mountDetailMetrics("epMetricsMount", {
      scope: "endpoint",
      route: ep.route,
      domain: ep.configuredDomain || undefined,
    });
    return;
  }

  paintPage(`
    <div id="epDetailHead">${endpointDetailHeadHtml(ep)}</div>
    <div id="epMetricsMount"></div>
    <p><a href="#/endpoints">← All endpoints</a>
      ${ep.configuredDomain ? ` · <a href="#/operations?domain=${encodeURIComponent(ep.configuredDomain)}">Operations for domain</a>` : ""}
    </p>`, soft);

  main().querySelectorAll("tr.clickable[data-id]").forEach((tr) => {
    tr.addEventListener("click", () => navigate("instances", { id: tr.dataset.id }));
  });
  mountDetailMetrics("epMetricsMount", {
    scope: "endpoint",
    route: ep.route,
    domain: ep.configuredDomain || undefined,
  });
}

function endpointDetailHeadHtml(ep) {
  const domainBadge = ep.configuredDomain
    ? currentValueHtml(`<a class="badge" href="#/domains?name=${encodeURIComponent(ep.configuredDomain)}">${esc(ep.configuredDomain)}</a>`)
    : "";
  return `
    <div class="card">
      <h2><code>${esc(ep.route)}</code>
        ${domainBadge}
        ${hintBadges(ep.hints)}
      </h2>
      ${recommendationsSectionHtml(ep.hints)}
      <div class="kpi-row">
        <div class="kpi" title="Current value (not part of the selected time range)"><div class="label">Domain</div><div class="value" style="font-size:1rem">${ep.configuredDomain ? currentValueHtml(esc(ep.configuredDomain)) : "—"}</div></div>
        <div class="kpi" title="${esc(METRIC_TITLES.req)}"><div class="label">Requests</div><div class="value">${num(ep.requests)}</div></div>
        <div class="kpi"${tipAttr("ocHitShare")}><div class="label">OC hit %</div><div class="value">${pct(ep.oc?.hitShare, ep.oc?.lowRequestSample, "request")}</div></div>
        <div class="kpi"${tipAttr("fcHitShare")}><div class="label">FC hit %</div><div class="value">${pct(ep.fc?.hitShare, ep.fc?.lowRequestSample, "request")}</div></div>
        <div class="kpi"${tipAttr("factoryShare")}><div class="label">Factory %</div><div class="value">${pct(factoryShareOf(ep.fc), ep.fc?.lowRequestSample, "request")}</div></div>
        ${impactKpiRowHtml(ep.impact)}
      </div>
      <p class="muted">Pipeline</p>
      ${pipelineBar(ep.pipeline, true)}
    </div>
    <div class="detail-grid">
      ${layerDetailOc(ep.oc)}
      ${layerDetailFc(ep.fc)}
      ${impactDetailHtml(ep.impact)}
    </div>
    ${ep.byInstance?.length ? `
    <div class="card">
      <h2>By instance</h2>
      <table class="dense">
        <thead><tr>
          ${thMetric("Instance", "instance", { fromKey: true })}
          ${thMetric("Req", "req", { fromKey: true })}
          ${thMetric("OC hit %", "ocHitShare", { fromKey: true })}
          ${thMetric("FC hit %", "fcHitShare", { fromKey: true })}
          ${thMetric("Factory %", "factoryShare", { fromKey: true })}
          ${thMetric("Time saved", "estTimeSaved", { fromKey: true })}
          ${thMetric("Benefit", "cacheBenefit", { fromKey: true })}
          ${thMetric("Candidate", "cacheCandidate", { fromKey: true })}
        </tr></thead>
        <tbody>
          ${ep.byInstance.map((bi) => `
            <tr class="clickable" data-id="${esc(bi.instanceId)}">
              <td><code>${esc(bi.instanceId)}</code></td>
              <td>${num(bi.requests)}</td>
              <td>${pct(bi.oc?.hitShare, bi.oc?.lowRequestSample, "request")}</td>
              <td>${pct(bi.fc?.hitShare, bi.fc?.lowRequestSample, "request")}</td>
              <td>${pct(factoryShareOf(bi.fc), bi.fc?.lowRequestSample, "request")}</td>
              <td>${fmtDurationMs(bi.impact?.estFactoryTimeSavedMs)}</td>
              <td>${impactBandLabel(bi.impact?.benefit, { html: true })}</td>
              <td>${impactBandLabel(bi.impact?.candidate, { html: true })}</td>
            </tr>`).join("")}
        </tbody>
      </table>
    </div>` : ""}`;
}

// —— Domains ——

export async function renderDomainsList(params, opts = {}) {
  const soft = !!opts.soft;
  setBreadcrumb([]);
  const search = params.get("search") || "";
  const sort = params.get("sort") || "requests";

  beginPageLoad(soft, `<div class="card"><p class="muted">Loading domains…</p></div>`);

  let instanceList = [];
  try {
    instanceList = await api("/api/instances");
  } catch (err) {
    if (soft && mainHasContent()) return;
    paintPage(`<div class="card">${emptyStateHtml("error", { detail: err.message })}</div>`, soft);
    bindEmptyStateActions(main());
    return;
  }

  let domains = [];
  let loadError = null;
  let promOk = false;
  try {
    const w = await fetchWindowStatsIfNeeded();
    if (w?.status === "Connected") {
      promOk = true;
      domains = w.domains || [];
      try {
        const cfgFan = await api("/api/domains");
        const ver = Object.fromEntries((cfgFan.data || []).map((d) => [d.name, d]));
        domains = domains.map((d) => {
          const a = ver[d.name];
          return a ? { ...d, version: a.version || d.version, versionIsRuntimeOverride: a.versionIsRuntimeOverride } : d;
        });
      } catch { /* optional version overlay */ }
    } else {
      loadError = w?.error || "Metrics not connected.";
    }
  } catch (err) {
    loadError = err.message;
  }

  domains = sortDomains(filterDomainsBySearch(domains, search), sort);
  const emptyKind = !promOk ? "metrics-config" : loadError ? "error" : "domains";
  const tableHtml = promOk
    ? domainTableHtml(domains, {
      kind: emptyKind,
      title: loadError ? "Failed to load domains" : undefined,
      detail: loadError,
    })
    : metricsRequiredEmpty(loadError);

  if (soft && $("#domRoot")) {
    const banner = $("#domBanner");
    if (banner) banner.innerHTML = connectivityBanner(instanceList);
    const table = $("#domTable");
    if (table) table.innerHTML = tableHtml;
    bindEmptyStateActions(main());
    bindEntityTableClicks($("#domTable") || main());
    return;
  }

  paintPage(`
    <div id="domRoot">
    <div id="domBanner">${connectivityBanner(instanceList)}</div>
    <div class="card">
      <h2>Domains</h2>
      ${promOk ? `
      <form class="toolbar" id="domFilters">
        <label>Search<input name="search" type="search" value="${esc(search)}" placeholder="domain name" /></label>
        ${sortSelectHtml("sort", sort, DOMAIN_SORT_OPTS)}
        ${applyButtonHtml()}
      </form>` : ""}
      <div id="domTable">${tableHtml}</div>
    </div>
    </div>`, soft);

  bindEmptyStateActions(main());
  const form = $("#domFilters");
  if (form) {
    form.addEventListener("submit", (ev) => {
      ev.preventDefault();
      const fd = new FormData(form);
      navigate("domains", {
        search: fd.get("search"),
        sort: fd.get("sort"),
      });
    });
  }
  bindEntityTableClicks($("#domTable") || main());
}

export async function renderDomainDetail(name, params = new URLSearchParams(), opts = {}) {
  const soft = !!opts.soft;
  const epSort = params.get("epSort") || "requests";
  setBreadcrumb([
    { label: "Domains", href: "#/domains" },
    { label: name },
  ]);
  beginPageLoad(soft, `<p class="muted">Loading domain ${esc(name)}…</p>`);

  const [cfgFan, windowStats] = await Promise.all([
    api("/api/domains").catch(() => ({ data: [] })),
    fetchWindowStatsIfNeeded(),
  ]);
  let d = windowStats?.status === "Connected"
    ? (windowStats.domains || []).find((x) => x.name === name)
    : null;
  const cfg = (cfgFan.data || []).find((x) => x.name === name);
  if (d && cfg && (!d.version || d.version === "")) {
    d = { ...d, version: cfg.version || "", versionIsRuntimeOverride: cfg.versionIsRuntimeOverride };
  }

  if (!d && !cfg) {
    if (!(soft && mainHasContent())) paintPage(`<div class="card"><p class="status-Down">Domain not found</p></div>`, soft);
    return;
  }

  const domain = d || { name, requests: 0, oc: {}, fc: {}, pipeline: {}, endpoints: [], hints: [] };
  const endpointsSorted = sortEndpoints(domain.endpoints || [], epSort);

  if (soft && $("#domMetricsMount")?.dataset?.metricsReady === "1") {
    const head = $("#domDetailHead");
    if (head) head.innerHTML = domainDetailHeadHtml(name, domain, cfg);
    const epHost = $("#domEpTable");
    if (epHost) {
      epHost.innerHTML = endpointTableHtml(endpointsSorted);
    }
    bindEntityTableClicks(main());
    main().querySelectorAll("tr.clickable[data-id]").forEach((tr) => {
      tr.addEventListener("click", () => navigate("instances", { id: tr.dataset.id }));
    });
    mountDetailMetrics("domMetricsMount", { scope: "domain", domain: name });
    return;
  }

  paintPage(`
    <div id="domDetailHead">${domainDetailHeadHtml(name, domain, cfg)}</div>
    <div class="card">
      <div class="card-head">
        <h2>Endpoints in domain</h2>
        ${inlineSortSelectHtml("domEpSort", epSort, EP_SORT_OPTS)}
      </div>
      <div id="domEpTable">${endpointTableHtml(endpointsSorted)}</div>
    </div>
    <div id="domMetricsMount"></div>
    <p><a href="#/domains">← Domains</a> · <a href="#/operations?domain=${encodeURIComponent(name)}">Operations</a></p>`, soft);

  main().querySelectorAll("tr.clickable[data-id]").forEach((tr) => {
    tr.addEventListener("click", () => navigate("instances", { id: tr.dataset.id }));
  });
  bindEntityTableClicks(main());
  $("#domEpSort")?.addEventListener("change", (ev) => {
    navigate("domains", { name, epSort: ev.target.value });
  });
  mountDetailMetrics("domMetricsMount", { scope: "domain", domain: name });
}

function domainDetailHeadHtml(name, domain, cfg) {
  const ver = domain.version || cfg?.version || "—";
  const clientTtl = `${fmtUnit(cfg?.clientTtlSeconds, "s")} / ${fmtUnit(cfg?.clientTtlMinSeconds, "s")}`;
  const hasConfig = !!(cfg || (ver && ver !== "—"));
  const verRt = !!(domain.versionIsRuntimeOverride || cfg?.versionIsRuntimeOverride);
  return `
    <div class="card">
      <h2><code>${esc(name)}</code>
        ${verRt ? '<span class="badge">runtime version</span>' : ""}
        ${hintBadges(domain.hints)}
        <a class="badge" href="#/operations?domain=${encodeURIComponent(name)}">Operations</a>
      </h2>
      ${recommendationsSectionHtml(domain.hints)}
      <div class="kpi-row">
        <div class="kpi" title="${esc(METRIC_TITLES.inv)}"><div class="label">Invalidations</div><div class="value">${num(domain.invalidations)}</div></div>
        <div class="kpi" title="${esc(METRIC_TITLES.req)}"><div class="label">Requests</div><div class="value">${num(domain.requests)}</div></div>
        <div class="kpi"${tipAttr("ocHitShare")}><div class="label">OC hit %</div><div class="value">${pct(domain.oc?.hitShare, domain.oc?.lowRequestSample, "request")}</div></div>
        <div class="kpi"${tipAttr("fcHitShare")}><div class="label">FC hit %</div><div class="value">${pct(domain.fc?.hitShare, domain.fc?.lowRequestSample, "request")}</div></div>
        <div class="kpi"${tipAttr("factoryShare")}><div class="label">Factory %</div><div class="value">${pct(factoryShareOf(domain.fc), domain.fc?.lowRequestSample, "request")}</div></div>
        ${impactKpiRowHtml(domain.impact)}
      </div>
      <p class="muted">Pipeline</p>
      ${pipelineBar(domain.pipeline, true)}
    </div>
    <div class="detail-grid">
      ${layerDetailOc(domain.oc)}
      ${layerDetailFc(domain.fc)}
      ${impactDetailHtml(domain.impact)}
      ${hasConfig ? `
      <div class="detail-block">
        <h3>Effective config <span class="badge" title="Current values (not part of the selected time range)">current</span></h3>
        <div class="kv">
          <span title="Domain version">Version</span>${currentValueHtml(esc(ver))}${verRt ? " *" : ""}
          ${cfg ? `
          <span${tipAttr("oc")}>Output TTL</span>${currentValueHtml(fmtUnit(cfg.outputCacheTtlSeconds, "s"))}
          <span${tipAttr("softTtl")}>Fusion soft</span>${currentValueHtml(fmtUnit(cfg.fusionCacheSoftTtlSeconds, "s"))}
          <span${tipAttr("hardTtl")}>Fusion hard</span>${currentValueHtml(fmtUnit(cfg.fusionCacheHardTtlSeconds, "s"))}
          <span${tipAttr("failSafe")}>Fail-safe</span>${currentValueHtml(fmtUnit(cfg.fusionCacheFailSafeSeconds, "s"))}
          <span${tipAttr("clientTtl")}>Client TTL / min</span>${currentValueHtml(clientTtl)}
          <span${tipAttr("schedule")}>Schedule phase</span>${currentValueHtml(esc(cfg.schedulePhase || "—"))}
          <span${tipAttr("fc")}>FC instance</span>${currentValueHtml(esc(cfg.fusionCacheInstanceName || "—"))}
          ` : ""}
        </div>
      </div>` : ""}
    </div>
    ${domain.byInstance?.length ? `
    <div class="card">
      <h2>By instance</h2>
      <table class="dense">
        <thead><tr>
          ${thMetric("Instance", "instance", { fromKey: true })}
          ${thMetric("Version", "version", { fromKey: true })}
          ${thMetric("Req", "req", { fromKey: true })}
          ${thMetric("Inv", "inv", { fromKey: true })}
          ${thMetric("OC hit %", "ocHitShare", { fromKey: true })}
          ${thMetric("FC hit %", "fcHitShare", { fromKey: true })}
          ${thMetric("Factory %", "factoryShare", { fromKey: true })}
          ${thMetric("Time saved", "estTimeSaved", { fromKey: true })}
          ${thMetric("Benefit", "cacheBenefit", { fromKey: true })}
          ${thMetric("Candidate", "cacheCandidate", { fromKey: true })}
        </tr></thead>
        <tbody>
          ${domain.byInstance.map((bi) => `
            <tr class="clickable" data-id="${esc(bi.instanceId)}">
              <td><code>${esc(bi.instanceId)}</code></td>
              <td>${currentValueHtml(esc(bi.version || "—"))}${bi.versionIsRuntimeOverride ? " *" : ""}</td>
              <td>${num(bi.requests)}</td>
              <td>${num(bi.invalidations)}</td>
              <td>${pct(bi.oc?.hitShare, bi.oc?.lowRequestSample, "request")}</td>
              <td>${pct(bi.fc?.hitShare, bi.fc?.lowRequestSample, "request")}</td>
              <td>${pct(factoryShareOf(bi.fc), bi.fc?.lowRequestSample, "request")}</td>
              <td>${fmtDurationMs(bi.impact?.estFactoryTimeSavedMs)}</td>
              <td>${impactBandLabel(bi.impact?.benefit, { html: true })}</td>
              <td>${impactBandLabel(bi.impact?.candidate, { html: true })}</td>
            </tr>`).join("")}
        </tbody>
      </table>
    </div>` : ""}`;
}

// —— Instances ——

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
            <h2 title="Metrics backend for Live, tables, and charts">Metrics</h2>
            <a href="#/metrics">Open Metrics →</a>
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
    <div class="card">
      <h2 id="instHead">Instances ${severityStack(hintSum)}</h2>
      <form class="toolbar" id="instFilters">
        <label>Search<input name="search" type="search" value="${esc(search)}" placeholder="id or url" /></label>
        ${sortSelectHtml("sort", sort, INST_SORT_OPTS)}
        ${applyButtonHtml()}
      </form>
      <div id="instTable">${tableHtml}</div>
    </div>
    <div id="instMetricsHost">${metricsCard}</div>
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
      · <a href="#/operations?target=instance:${encodeURIComponent(id)}">Operations on this instance</a></p>`, soft);

  bindEntityTableClicks(main());
  $("#instDomSort")?.addEventListener("change", (ev) => {
    navigate("instances", { id, domSort: ev.target.value, epSort });
  });
  $("#instEpSort")?.addEventListener("change", (ev) => {
    navigate("instances", { id, domSort, epSort: ev.target.value });
  });
  mountDetailMetrics("instMetricsMount", { scope: "instance", instanceId: id });
}

function instanceDetailHeadHtml(id, inst, stats, st, startedTitle, promOk = false) {
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
        <div class="kpi" title="Instance health">
          <div class="label">Status</div>
          <div class="value status-${esc(st)}" style="font-size:1.05rem">${esc(st)}</div>
        </div>
        <div class="kpi" title="${esc(startedTitle)}"><div class="label">Uptime</div><div class="value col-uptime" style="font-size:1.05rem">${esc(formatUptime(inst?.uptimeSeconds))}</div></div>
        <div class="kpi"><div class="label">Started (UTC)</div><div class="value" style="font-size:0.85rem">${esc(startedDisp)}</div></div>
        <div class="kpi" title="Admin probe latency"><div class="label">Latency</div><div class="value" style="font-size:1.05rem">${formatLatencyMs(inst?.latencyMs)}</div></div>
        <div class="kpi"><div class="label">Req</div><div class="value">${promOk ? num(reqFromDomains) : noDataHtml()}</div></div>
      </div>
    </div>`;
}

// —— Hints ——

export async function renderHintsPage(params, opts = {}) {
  const soft = !!opts.soft;
  setBreadcrumb([]);
  const selDomains = parseCsvParam(params, "domains");
  const selEndpoints = parseCsvParam(params, "endpoints");
  const severity = params.get("severity") || "";

  beginPageLoad(soft, `<div class="card"><p class="muted">Loading hints…</p></div>`);

  const windowStats = await fetchWindowStatsIfNeeded();

  const promOk = windowStats?.status === "Connected";
  const statsForHints = promOk
    ? { domains: windowStats.domains || [], endpoints: windowStats.endpoints || [] }
    : { domains: [], endpoints: [] };

  const domainOpts = (statsForHints.domains || []).map((d) => ({ id: d.name, label: d.name }));
  const endpointOpts = (statsForHints.endpoints || []).map((e) => ({ id: e.route, label: e.route }));

  let rows = collectHintRows(statsForHints);
  const totalSummary = summarizeHints(rows);
  shell.updateNavHintsBadge(promOk && windowStats?.hintSummary ? windowStats.hintSummary : totalSummary);

  const filtersActive = selDomains !== null || selEndpoints !== null || !!severity;

  if (selDomains !== null) {
    if (selDomains.length === 0) rows = [];
    else rows = rows.filter((r) => !r.domain || selDomains.includes(r.domain));
  }
  if (selEndpoints !== null) {
    if (selEndpoints.length === 0) rows = [];
    else rows = rows.filter((r) => !r.route || selEndpoints.includes(r.route));
  }
  if (severity) {
    rows = rows.filter((r) => r.severity === severity);
  }

  const shownSummary = summarizeHints(rows);
  const ratio = (shown, total) => (filtersActive ? `${shown}/${total}` : String(shown));

  const rank = { Critical: 0, Warning: 1, Info: 2 };
  rows.sort((a, b) => (rank[a.severity] ?? 9) - (rank[b.severity] ?? 9) || a.code.localeCompare(b.code));

  const headHtml = `Hints ${severityStack(filtersActive ? shownSummary : totalSummary)}
        ${filtersActive ? `<span class="badge muted" title="Visible / all">${shownSummary.total}/${totalSummary.total}</span>` : ""}`;
  const kpiHtml = `
      <div class="kpi-row" id="hintKpis">
        <div class="kpi"><div class="label">Critical</div><div class="value status-Down">${ratio(shownSummary.critical, totalSummary.critical)}</div></div>
        <div class="kpi"><div class="label">Warning</div><div class="value" style="color:var(--warn)">${ratio(shownSummary.warning, totalSummary.warning)}</div></div>
        <div class="kpi"><div class="label">Info</div><div class="value" style="color:var(--accent)">${ratio(shownSummary.info, totalSummary.info)}</div></div>
        <div class="kpi"><div class="label">Shown</div><div class="value">${ratio(shownSummary.total, totalSummary.total)}</div></div>
      </div>`;
  const tableHtml = !promOk
    ? metricsRequiredEmpty()
    : rows.length
      ? `
      <table class="dense entity-table hints-table">
        <thead>
          <tr>
            <th>Sev</th><th>Code</th><th>Message</th>
            <th>Instance</th><th>Domain</th><th>Endpoint</th><th>Entity</th>
          </tr>
        </thead>
        <tbody>
          ${rows.map((r) => `
            <tr class="hint-table-row ${esc(r.severity)}">
              <td><span class="hint ${esc(r.severity)}">${esc(r.severity)}</span></td>
              <td><code>${esc(r.code)}</code></td>
              <td>${esc(r.message)}</td>
              <td>${r.instanceId ? `<a href="#/instances?id=${encodeURIComponent(r.instanceId)}"><code>${esc(r.instanceId)}</code></a>` : "—"}</td>
              <td>${r.domain ? `<a href="#/domains?name=${encodeURIComponent(r.domain)}"><code>${esc(r.domain)}</code></a>` : "—"}</td>
              <td>${r.route ? `<a href="#/endpoints?route=${encodeURIComponent(r.route)}"><code>${esc(r.route)}</code></a>` : "—"}</td>
              <td class="muted">${esc(r.entityType)}</td>
            </tr>`).join("")}
        </tbody>
      </table>`
      : emptyStateHtml("filter", {
        title: "No hints to show",
        detail: "No recommendations for the current filters.",
      });

  if (soft && $("#hintsRoot") && promOk) {
    const head = $("#hintsHead");
    if (head) head.innerHTML = headHtml;
    const kpis = $("#hintKpis");
    if (kpis) kpis.outerHTML = kpiHtml;
    const table = $("#hintsTable");
    if (table) table.innerHTML = tableHtml;
    bindEmptyStateActions(main());
    return;
  }

  paintPage(`
    <div id="hintsRoot" class="card">
      <h2 id="hintsHead">${headHtml}</h2>
      <p class="muted">${promOk
        ? "Recommendations for the selected time range."
        : "Connect metrics to see recommendations."}
        Filters combine (AND).
        ${filtersActive ? " Severity counts show <strong>visible/total</strong> for the current filter." : ""}
      </p>
      ${!promOk ? tableHtml : `
      <form class="toolbar" id="hintFilters">
        ${multiSelectHtml("hDom", "Domains", domainOpts, selDomains)}
        ${multiSelectHtml("hEp", "Endpoints", endpointOpts, selEndpoints)}
        <label>Severity
          <select name="severity">
            <option value="">All</option>
            ${["Critical", "Warning", "Info"].map((s) =>
              `<option value="${s}" ${severity === s ? "selected" : ""}>${s}</option>`).join("")}
          </select>
        </label>
        ${applyButtonHtml()}
      </form>
      ${kpiHtml}
      <div id="hintsTable">${tableHtml}</div>`}
    </div>`, soft);
  bindEmptyStateActions(main());

  const form = $("#hintFilters");
  if (form) {
    bindMultiSelects(form);
    form.addEventListener("submit", (ev) => {
      ev.preventDefault();
      const fd = new FormData(form);
      navigate("hints", {
        domains: csvParamFromSelection(readMultiSelect(form, "hDom")),
        endpoints: csvParamFromSelection(readMultiSelect(form, "hEp")),
        severity: fd.get("severity") || "",
      });
    });
  }
}

// —— Operations ——

export async function renderOperations(params) {
  setBreadcrumb([]);
  const domain = params.get("domain") || "hello";
  const target = params.get("target") || "all";
  const action = params.get("action") || "invalidate";

  const [instances, distribution] = await Promise.all([
    api("/api/instances"),
    api("/api/distribution").catch(() => null),
  ]);

  const mode = distribution?.recommendedMode || "fan-out";
  const busAvailable = !!distribution?.busAvailable;
  const modeClass = mode === "bus-distribute" ? "mode-bus" : "mode-fanout";
  const modeLabel = mode === "bus-distribute" ? "Cluster bus" : "Direct to each instance";
  const modeDetail = distribution?.summary
    || "How this Console will deliver the operation.";

  const probeRows = (distribution?.instances || []).map((p) => {
    const bus = p.busEnabled
      ? `<span class="badge ok">bus</span>`
      : `<span class="badge muted">no bus</span>`;
    const mem = p.membership ? esc(p.membership) : "—";
    const peers = p.peerCount != null ? p.peerCount : "—";
    const st = p.succeeded ? "ok" : "bad";
    return `<tr>
      <td>${esc(p.id)}</td>
      <td class="${st}">${p.succeeded ? "reachable" : "down"}</td>
      <td>${bus}</td>
      <td>${mem}</td>
      <td>${peers}</td>
      <td class="muted" title="${p.error ? esc(p.error) : ""}">${p.error ? esc(shortError(p.error)) : ""}</td>
    </tr>`;
  }).join("");

  main().innerHTML = `
    <div class="card">
      <h2>Operations</h2>
      <div id="distBanner" class="dist-banner ${modeClass}">
        <div class="dist-banner-title">
          <span class="badge ${mode === "bus-distribute" ? "ok" : "warn"}">${esc(modeLabel)}</span>
          ${busAvailable && distribution?.preferredBusOriginId
            ? `<span class="muted">preferred origin: <code>${esc(distribution.preferredBusOriginId)}</code></span>`
            : ""}
        </div>
        <p class="muted dist-banner-detail">${esc(modeDetail)}</p>
        <p class="muted small">
          <strong>Direct</strong> — this Console calls each selected instance.
          <strong>Cluster bus</strong> — one origin receives the command; peers apply it via the bus.
        </p>
      </div>
      <form id="opForm" class="form-grid">
        <label>Action
          <select id="opAction" name="action">
            <option value="invalidate" ${action === "invalidate" ? "selected" : ""}>Invalidate domain</option>
            <option value="entity" ${action === "entity" ? "selected" : ""}>Invalidate entity</option>
            <option value="version" ${action === "version" ? "selected" : ""}>Bump version</option>
            <option value="ttl" ${action === "ttl" ? "selected" : ""}>Patch TTL</option>
          </select>
        </label>
        <label>Domain
          <input id="opDomain" name="domain" type="text" value="${esc(domain)}" required />
        </label>
        <label id="entityKindLabel" class="${action === "entity" ? "" : "hidden"}">Entity kind
          <input id="opEntityKind" type="text" placeholder="products" />
        </label>
        <label id="entityLabel" class="${action === "entity" ? "" : "hidden"}">Entity id
          <input id="opEntity" type="text" placeholder="resource id" />
        </label>
        <label>Target
          <select id="opTarget" name="target">
            <option value="all" ${target === "all" ? "selected" : ""}>all</option>
            ${instances.map((i) =>
              `<option value="instance:${esc(i.id)}" ${target === `instance:${i.id}` ? "selected" : ""}>instance:${esc(i.id)}</option>`
            ).join("")}
          </select>
        </label>
        <label id="versionLabel" class="${action === "version" ? "" : "hidden"}">Version (optional)
          <input id="opVersion" type="text" placeholder="auto if empty" />
        </label>
        <label id="ttlLabel" class="${action === "ttl" ? "" : "hidden"}">OutputCacheTtlSeconds
          <input id="opTtl" type="number" min="0" value="120" />
        </label>
        <label id="ttlSoftLabel" class="${action === "ttl" ? "" : "hidden"}">Fusion soft TTL (optional)
          <input id="opTtlSoft" type="number" min="0" placeholder="leave empty" />
        </label>
        <button type="submit">Run</button>
      </form>
      <div id="opModeUsed" class="dist-result-meta muted">No operation yet.</div>
      <pre id="opResult" class="result">No operation yet.</pre>
    </div>
    <div class="card">
      <h2>Cluster bus probe</h2>
      <p class="muted">Bus capability reported by each configured instance.</p>
      <div class="table-wrap">
        <table class="data">
          <thead>
            <tr><th>Instance</th><th>Probe</th><th>Bus</th><th>Membership</th><th>Peers</th><th>Error</th></tr>
          </thead>
          <tbody>
            ${probeRows || `<tr><td colspan="6" class="muted">No instances configured.</td></tr>`}
          </tbody>
        </table>
      </div>
    </div>
    <div class="card">
      <h2>Quick links</h2>
      <p class="muted">
        <a href="#/domains">Domains</a> ·
        <a href="#/instances">Instances</a>
      </p>
    </div>`;

  const actionEl = $("#opAction");
  function syncOpFields() {
    const a = actionEl.value;
    $("#entityKindLabel").classList.toggle("hidden", a !== "entity");
    $("#entityLabel").classList.toggle("hidden", a !== "entity");
    $("#versionLabel").classList.toggle("hidden", a !== "version");
    $("#ttlLabel").classList.toggle("hidden", a !== "ttl");
    $("#ttlSoftLabel").classList.toggle("hidden", a !== "ttl");
  }
  actionEl.addEventListener("change", syncOpFields);

  function renderModeUsed(result) {
    const meta = $("#opModeUsed");
    if (!result) {
      meta.textContent = "No operation yet.";
      return;
    }
    const m = result.distributionMode || "fan-out";
    const badge = m === "bus-distribute"
      ? `<span class="badge ok">cluster bus</span>`
      : `<span class="badge warn">direct</span>`;
    const origin = result.busOriginInstanceId
      ? ` · origin <code>${esc(result.busOriginInstanceId)}</code>`
      : "";
    const dist = result.distribute ? "distribute:true" : "distribute:false";
    meta.innerHTML = `${badge} · ${dist}${origin}<br/><span class="muted">${esc(result.distributionSummary || "")}</span>`;
  }

  $("#opForm").addEventListener("submit", async (ev) => {
    ev.preventDefault();
    const a = actionEl.value;
    const dom = $("#opDomain").value.trim();
    const tgt = $("#opTarget").value;
    const out = $("#opResult");
    out.textContent = "Running…";
    $("#opModeUsed").textContent = "Running…";
    try {
      let result;
      if (a === "invalidate") {
        result = await api("/api/invalidate", {
          method: "POST",
          body: JSON.stringify({ scope: "domain", domain: dom, target: tgt }),
        });
      } else if (a === "entity") {
        result = await api("/api/invalidate", {
          method: "POST",
          body: JSON.stringify({
            scope: "entity",
            domain: dom,
            entityKind: $("#opEntityKind").value.trim(),
            entityId: $("#opEntity").value.trim(),
            target: tgt,
          }),
        });
      } else if (a === "version") {
        const version = $("#opVersion").value.trim();
        result = await api(`/api/domains/${encodeURIComponent(dom)}/version`, {
          method: "POST",
          body: JSON.stringify({ version: version || null, target: tgt }),
        });
      } else {
        const body = {
          outputCacheTtlSeconds: Number($("#opTtl").value),
          target: tgt,
        };
        const soft = $("#opTtlSoft").value;
        if (soft !== "") body.fusionCacheSoftTtlSeconds = Number(soft);
        result = await api(`/api/domains/${encodeURIComponent(dom)}/ttl`, {
          method: "PATCH",
          body: JSON.stringify(body),
        });
      }
      renderModeUsed(result);
      out.textContent = JSON.stringify(result, null, 2);
      shell.refreshHeader();
    } catch (err) {
      $("#opModeUsed").textContent = "Error";
      out.textContent = "Error: " + err.message;
    }
  });
}

/** KPI color: green only when every configured instance is healthy. */
function instancesUpClass(o) {
  const total = (o.instances || []).length;
  const up = o.healthyCount ?? 0;
  const down = o.downCount ?? 0;
  const deg = o.degradedCount ?? 0;
  if (total === 0 || down > 0 || up < total) return "status-Down";
  if (deg > 0) return "status-Degraded";
  return "status-Healthy";
}

/** Truncate long probe errors (e.g. accidental HTML bodies) for the Operations table. */
function shortError(msg) {
  const s = String(msg || "").replace(/\s+/g, " ").trim();
  if (s.length <= 120) return s;
  return s.slice(0, 117) + "…";
}

// —— Settings (hint rules catalog) ——

export async function renderSettingsPage() {
  setNavActive("settings");
  setBreadcrumb([]);
  beginPageLoad(false, `<div class="card"><p class="muted">Loading settings…</p></div>`);

  let payload;
  try {
    payload = await api("/api/hints/rules");
  } catch (err) {
    paintPage(`<div class="card">${emptyStateHtml("error", { detail: err.message })}</div>`, false);
    bindEmptyStateActions(main());
    return;
  }

  const load = payload.load || {};
  const rules = payload.rules || [];
  /** @type {Map<string, any>} id -> rule */
  const ruleById = new Map((rules || []).map((r) => [r.id, r]));
  const errors = load.errors || [];
  const loadBadge = load.ok
    ? `<span class="badge ok">OK</span>`
    : `<span class="badge bad">ERROR</span>`;

  const errBlock = errors.length
    ? `<div class="card hint-compile-error-card">
        <div class="card-head">
          <h2><span class="badge bad">ERROR</span> Rule compile issues
            <span class="badge bad">${errors.length}</span></h2>
        </div>
        <p class="hint-compile-error-lead">
          ${errors.length} problem(s) in rule files — rules with errors were not loaded.
          Fix JSON, then <strong>Reload</strong>. <strong>Rule</strong> names the entry; <strong>Path</strong> is inside that rule.
        </p>
        <table class="dense hint-compile-error-table">
          <thead><tr><th>File</th><th>Rule</th><th>Path</th><th>Message</th></tr></thead>
          <tbody>
            ${errors.map((e) => `
              <tr>
                <td><code>${esc(e.file || "")}</code></td>
                <td>${e.ruleCode ? `<code>${esc(e.ruleCode)}</code>` : `<span class="muted">—</span>`}</td>
                <td><code>${esc(e.path || "")}</code></td>
                <td>${esc(e.message || "")}</td>
              </tr>`).join("")}
          </tbody>
        </table>
      </div>`
    : "";

  const groups = groupHintRulesBySource(rules);
  const groupHtml = groups.map(([source, list], gi) => {
    const open = gi === 0; // core pack open by default
    const label = sourceLabel(source);
    const coreBadge = list.some((r) => r.isBuiltIn) ? ` <span class="badge muted">core</span>` : "";
    return `
      <div class="hint-rule-group" data-source="${esc(source)}">
        <button type="button" class="hint-group-toggle" aria-expanded="${open ? "true" : "false"}">
          <span class="hint-group-chevron">${open ? "▼" : "▶"}</span>
          <span>${esc(label)}${coreBadge}</span>
          <span class="badge">${list.length}</span>
        </button>
        <div class="hint-group-body${open ? "" : " hidden"}">
          <div class="table-wrap">
            <table class="dense entity-table">
              <thead>
                <tr>
                  <th>Enabled</th><th>Code</th><th>Severity</th><th>Scope</th>
                  <th>Category</th><th>Description</th>
                </tr>
              </thead>
              <tbody>
                ${list.map((r) => `
                  <tr class="hint-rule-row clickable" data-rule-id="${esc(r.id)}" title="Click to view rule definition">
                    <td>
                      <input type="checkbox" class="hint-rule-enabled" data-code="${esc(r.code)}"
                        ${r.enabled ? "checked" : ""} title="Enable / disable ${esc(r.code)}" />
                    </td>
                    <td><code class="hint-rule-code-link">${esc(r.code)}</code></td>
                    <td>${severityCell(r.defaultSeverity)}</td>
                    <td>${esc(r.scope || "")}</td>
                    <td>${esc(r.category || "—")}</td>
                    <td class="muted">${esc(r.description || "")}</td>
                  </tr>`).join("")}
              </tbody>
            </table>
          </div>
        </div>
      </div>`;
  }).join("");

  paintPage(`
    <div class="card">
      <div class="card-head">
        <h2>Hint rules ${loadBadge}</h2>
        <div class="chart-card-actions">
          <button type="button" class="secondary" id="btnHintsReload">Reload files</button>
        </div>
      </div>
      <p class="muted">
        Product defaults: <code>hints/core-hints.json</code> (always loaded).
        Extra packs: <code>AdminConsole:Hints:RuleFiles</code>
        (Development: <code>hints/*.json</code>; Production/Docker: <code>data/rules/*.json</code>).
        Disable codes here (saved to <code>DisabledStatePath</code>, e.g. <code>hints/disabled.local.json</code>
        or <code>data/disabled.local.json</code>) or via <code>DisabledCodes</code> in config.
        See <code>hints/README.md</code>.
      </p>
      <p class="muted small">
        Loaded ${num(load.ruleCount)} rule(s) from ${num(load.fileCount)} file(s)
        · ${load.loadedAtUtc ? esc(String(load.loadedAtUtc)) : "—"}
        · Click a rule row to view its JSON definition.
      </p>
    </div>
    ${errBlock}
    <div class="card">
      <h2>Catalog <span class="badge">${rules.length}</span></h2>
      <p class="muted small">Groups are one per rule file. Click the header to collapse / expand.</p>
      <div id="hintRulesCatalog">${groupHtml || `<p class="muted">No rules loaded.</p>`}</div>
    </div>
    <div class="card">
      <h2>Known paths <span class="badge">${(payload.knownPaths || []).length}</span></h2>
      <p class="muted">Use these <code>path</code> values in declarative <code>when</code> conditions.</p>
      <p class="muted small" style="max-height:10rem;overflow:auto">
        ${(payload.knownPaths || []).map((p) => `<code>${esc(p)}</code>`).join(" · ")}
      </p>
    </div>`, false);

  main().querySelectorAll(".hint-group-toggle").forEach((btn) => {
    btn.addEventListener("click", () => {
      const body = btn.parentElement?.querySelector(".hint-group-body");
      const chev = btn.querySelector(".hint-group-chevron");
      if (!body) return;
      const open = body.classList.toggle("hidden");
      // classList.toggle returns true if class is now present; hidden => collapsed
      const expanded = !open;
      btn.setAttribute("aria-expanded", expanded ? "true" : "false");
      if (chev) chev.textContent = expanded ? "▼" : "▶";
    });
  });

  $("#btnHintsReload")?.addEventListener("click", async () => {
    try {
      await api("/api/hints/reload", { method: "POST", body: "{}" });
      await renderSettingsPage();
    } catch (err) {
      alert("Reload failed: " + err.message);
    }
  });

  main().querySelectorAll(".hint-rule-enabled").forEach((cb) => {
    cb.addEventListener("click", (ev) => ev.stopPropagation());
    cb.addEventListener("change", async () => {
      const code = cb.getAttribute("data-code");
      if (!code) return;
      cb.disabled = true;
      try {
        await api(`/api/hints/rules/${encodeURIComponent(code)}/enabled`, {
          method: "PUT",
          body: JSON.stringify({ enabled: !!cb.checked }),
        });
        // Same code may appear in domain + endpoint rows — keep checkboxes in sync.
        main().querySelectorAll(".hint-rule-enabled").forEach((el) => {
          if (el !== cb && el.getAttribute("data-code") === code) el.checked = cb.checked;
        });
      } catch (err) {
        cb.checked = !cb.checked;
        alert("Could not update rule: " + err.message);
      } finally {
        cb.disabled = false;
      }
    });
  });

  main().querySelectorAll("tr.hint-rule-row").forEach((tr) => {
    tr.addEventListener("click", (ev) => {
      if (ev.target?.closest?.("input, button, a, label")) return;
      const id = tr.getAttribute("data-rule-id");
      const rule = id ? ruleById.get(id) : null;
      if (rule) openHintRuleDetail(rule);
    });
  });
}

function severityCell(sev) {
  if (!sev) return `<span class="muted">—</span>`;
  const s = String(sev);
  const cls = s === "Critical" ? "sev-Critical" : s === "Warning" ? "sev-Warning" : "sev-Info";
  return `<span class="hint-sev-label ${cls}">${esc(s)}</span>`;
}

/* Standard “two sheets” copy icon (stroke, Lucide-style). */
const COPY_ICON_SVG = `<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><rect x="9" y="9" width="13" height="13" rx="2" ry="2"/><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/></svg>`;
const CLOSE_ICON_SVG = `<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><path d="M18 6 6 18"/><path d="m6 6 12 12"/></svg>`;

/** Modal with pretty-printed rule JSON. */
function openHintRuleDetail(rule) {
  closeHintRuleDetail();
  const title = `${rule.code || "rule"} · ${rule.scope || ""}`;
  let body = rule.definitionJson;
  if (!body) {
    body = JSON.stringify({
      code: rule.code,
      severity: rule.defaultSeverity,
      category: rule.category,
      scope: rule.scope,
      description: rule.description,
      source: rule.source,
      note: "Definition JSON not available for this rule.",
    }, null, 2);
  }
  const backdrop = document.createElement("div");
  backdrop.className = "chart-modal-backdrop";
  backdrop.id = "hintRuleDetailBackdrop";
  backdrop.innerHTML = `
    <div class="chart-modal hint-rule-detail-modal" role="dialog" aria-modal="true" aria-label="${esc(title)}">
      <div class="chart-modal-head">
        <h2>
          <code>${esc(rule.code || "")}</code>
          ${severityCell(rule.defaultSeverity)}
          <span class="badge muted">${esc(rule.scope || "")}</span>
        </h2>
        <div class="chart-modal-actions">
          <button type="button" class="secondary chart-modal-icon-btn hint-rule-copy" aria-label="Copy rule JSON" title="Copy rule JSON">${COPY_ICON_SVG}</button>
          <button type="button" class="secondary chart-modal-icon-btn chart-modal-close" aria-label="Close" title="Close">${CLOSE_ICON_SVG}</button>
        </div>
      </div>
      <p class="muted small" style="margin:0 0 0.5rem">
        ${esc(sourceLabel(rule.source || ""))}
        ${rule.category ? ` · ${esc(rule.category)}` : ""}
        ${rule.enabled === false ? ` · <span class="status-Down">disabled</span>` : ""}
      </p>
      <pre class="hint-rule-json result">${esc(body)}</pre>
    </div>`;
  document.body.appendChild(backdrop);
  document.body.classList.add("chart-modal-open");
  const close = () => closeHintRuleDetail();
  backdrop.querySelector(".chart-modal-close")?.addEventListener("click", close);
  const copyBtn = backdrop.querySelector(".hint-rule-copy");
  copyBtn?.addEventListener("click", async () => {
    const ok = await copyTextToClipboard(body);
    if (!copyBtn) return;
    copyBtn.classList.toggle("copied", ok);
    copyBtn.title = ok ? "Copied" : "Copy failed";
    copyBtn.setAttribute("aria-label", ok ? "Copied" : "Copy failed");
    window.setTimeout(() => {
      if (!copyBtn.isConnected) return;
      copyBtn.classList.remove("copied");
      copyBtn.title = "Copy rule JSON";
      copyBtn.setAttribute("aria-label", "Copy rule JSON");
    }, 1500);
  });
  backdrop.addEventListener("click", (ev) => {
    if (ev.target === backdrop) close();
  });
  const onKey = (ev) => {
    if (ev.key === "Escape") close();
  };
  backdrop._onKey = onKey;
  document.addEventListener("keydown", onKey);
}

/** @param {string} text */
async function copyTextToClipboard(text) {
  try {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(text);
      return true;
    }
  } catch {
    /* fall through */
  }
  try {
    const ta = document.createElement("textarea");
    ta.value = text;
    ta.setAttribute("readonly", "");
    ta.style.position = "fixed";
    ta.style.left = "-9999px";
    document.body.appendChild(ta);
    ta.select();
    const ok = document.execCommand("copy");
    ta.remove();
    return ok;
  } catch {
    return false;
  }
}

function closeHintRuleDetail() {
  const el = document.getElementById("hintRuleDetailBackdrop");
  if (!el) return;
  if (el._onKey) document.removeEventListener("keydown", el._onKey);
  el.remove();
  document.body.classList.remove("chart-modal-open");
}

/** @param {Array} rules */
function groupHintRulesBySource(rules) {
  /** @type {Map<string, any[]>} */
  const map = new Map();
  for (const r of rules || []) {
    const key = r.source || "unknown";
    if (!map.has(key)) map.set(key, []);
    map.get(key).push(r);
  }
  // core file first
  return [...map.entries()].sort((a, b) => {
    const ac = /core-hints/i.test(a[0]) ? 0 : 1;
    const bc = /core-hints/i.test(b[0]) ? 0 : 1;
    if (ac !== bc) return ac - bc;
    return a[0].localeCompare(b[0]);
  });
}

function sourceLabel(source) {
  const s = String(source || "");
  if (s.startsWith("file:")) return s.slice(5);
  return s;
}

// —— Route dispatch ——

/**
 * Map current hash to a view.
 * @param {{ soft?: boolean }} [opts] soft: background refresh without Loading flash
 */
export async function route(opts = {}) {
  const soft = !!opts.soft;
  const { path, params } = parseHash();
  const root = path.split("/")[0] || "overview";
  setNavActive(root);

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
