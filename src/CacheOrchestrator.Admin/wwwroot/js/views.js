/**
 * Page views (hash routes) for the Admin SPA.
 *
 * Each `render*` function owns its own data load + HTML + event bindings.
 * Shared tables / filters / hints live in sibling modules.
 */

import { api } from "./api.js";
import { $, main, mainHasContent, paintMain } from "./dom.js";
import {
  esc,
  formatLatencyMs,
  formatUptime,
  fmtUnit,
  num,
  pct,
  pipelineBar,
  spreadCell,
} from "./format.js";
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
import {
  collectHintRows,
  hintBadges,
  hintListHtml,
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
  instanceTableHtml,
  layerDetailFc,
  layerDetailOc,
  noInstancesConfigured,
} from "./tables.js";
import { metricsOverviewSectionHtml, mountDetailMetrics, renderMetrics } from "./views-metrics.js";

/** First paint may show loading; soft refresh keeps previous content until data arrives. */
function beginPageLoad(soft, loadingHtml) {
  if (!soft || !mainHasContent()) {
    main().innerHTML = loadingHtml;
  }
}

/** Soft refresh preserves scroll; hard navigation replaces immediately. */
function paintPage(html, soft) {
  if (soft) paintMain(html);
  else main().innerHTML = html;
}

// —— Overview ——

export async function renderOverview(params = new URLSearchParams(), opts = {}) {
  const soft = !!opts.soft;
  setBreadcrumb([{ label: "Overview" }]);

  // Prefer last overview for instant paint (header may already have fetched it).
  const cached = shell.getLastOverview();
  if (cached && (!soft || !mainHasContent())) {
    paintOverviewBody(cached, params, soft);
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
  shell.renderHeader(o);
  shell.updateNavHintsBadge(o.hintSummary);
  paintOverviewBody(o, params, soft);
}

function paintOverviewBody(o, params, soft) {

  const offline = allInstancesDown(o.instances);
  const noCfg = noInstancesConfigured(o.instances);
  const instSort = params.get("instSort") || "status";
  // Sort the full overview lists (all domains / all endpoints), then take top 5.
  // Default keys match “interesting” ranking (origin share, then traffic via sort helpers).
  const domSort = params.get("domSort") || "originShare";
  const epSort = params.get("epSort") || "originShare";
  const instancesSorted = sortInstances(o.instances || [], instSort);
  const top5Domains = sortDomains(o.topDomains || [], domSort).slice(0, 5);
  const top5Endpoints = sortEndpoints(o.topEndpoints || [], epSort).slice(0, 5);

  const offlineDetail =
    "Start target apps with Cache:Admin:Enabled and matching ApiKey, then refresh.";

  const tableKind = noCfg ? "config" : offline ? "offline" : "domains";
  const epKind = noCfg ? "config" : offline ? "offline" : "endpoints";

  // Soft refresh: patch live regions only so charts / tables do not remount every interval.
  if (soft && $("#ovRoot")) {
    const bannerHost = $("#ovBannerHost");
    if (bannerHost) bannerHost.innerHTML = connectivityBanner(o.instances);
    const kpis = $("#ovKpis");
    if (kpis) kpis.innerHTML = overviewKpiHtml(o);
    const pipe = $("#ovPipeline");
    if (pipe) pipe.innerHTML = pipelineBar(o.pipeline, true);
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
    <div class="kpi-row" id="ovKpis">${overviewKpiHtml(o)}</div>
    <div class="card">
      <h2>Cluster pipeline</h2>
      <div id="ovPipeline">${pipelineBar(o.pipeline, true)}</div>
      <p class="muted" style="margin:0.5rem 0 0;font-size:0.85rem">OC hit · FC hit · Origin · Bypass — shares of total requests</p>
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
      ${!offline && (o.topDomains || []).length ? `<p style="margin:0.75rem 0 0"><a href="#/domains">All domains →</a></p>` : ""}
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
      ${!offline && (o.topEndpoints || []).length ? `<p style="margin:0.75rem 0 0"><a href="#/endpoints">All endpoints →</a></p>` : ""}
    </div>
    <div id="ovMetricsMount"><p class="muted small">Checking metrics store…</p></div>
    </div>`, soft);

  bindEntityTableClicks(main());
  bindEmptyStateActions(main());

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

function overviewKpiHtml(o) {
  return `
      <div class="kpi"><div class="label">Instances up</div><div class="value ${instancesUpClass(o)}">${o.healthyCount}/${(o.instances || []).length}\u2009up</div></div>
      <div class="kpi"><div class="label">Cluster hints</div><div class="value">${severityStack(o.hintSummary)}</div></div>
      <div class="kpi"><div class="label">Requests</div><div class="value">${num(o.totalRequests)}</div></div>
      <div class="kpi"><div class="label">OC hit share</div><div class="value">${pct(o.ocHitShare)}</div></div>
      <div class="kpi"><div class="label">Origin share</div><div class="value">${pct(o.originShare)}</div></div>
      <div class="kpi"><div class="label">Domains / EP</div><div class="value" style="font-size:1rem">${num(o.domainCount)} / ${num(o.endpointCount)}</div></div>`;
}

// —— Endpoints ——

export async function renderEndpointsList(params, opts = {}) {
  const soft = !!opts.soft;
  setBreadcrumb([{ label: "Endpoints", href: "#/endpoints" }]);
  const search = params.get("search") || "";
  const sort = params.get("sort") || "requests";
  const take = params.get("take") || "50";
  const skip = Number(params.get("skip") || "0");
  const selInstances = parseCsvParam(params, "instances");
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

  const offline = allInstancesDown(instanceList);
  const noCfg = noInstancesConfigured(instanceList);
  const instanceOpts = (instanceList || []).map((i) => ({ id: i.id, label: i.id }));

  let domainOpts = [];
  let list = [];
  let loadError = null;
  if (!noCfg && !offline && !(selDomains !== null && selDomains.length === 0)
      && !(selInstances !== null && selInstances.length === 0)) {
    try {
      const statsForFilters = await api("/api/stats?scope=all");
      domainOpts = (statsForFilters.domains || []).map((d) => ({ id: d.name, label: d.name }));
      const q = new URLSearchParams({ sort, take, skip: String(skip), search });
      if (selInstances !== null) q.set("instances", selInstances.length ? selInstances.join(",") : "__none__");
      if (selDomains !== null) q.set("domains", selDomains.length ? selDomains.join(",") : "__none__");
      list = await api("/api/endpoints?" + q.toString());
    } catch (err) {
      loadError = err.message;
    }
  } else if (!noCfg && !offline) {
    try {
      const statsForFilters = await api("/api/stats?scope=all");
      domainOpts = (statsForFilters.domains || []).map((d) => ({ id: d.name, label: d.name }));
    } catch { /* filters optional */ }
  }

  const emptyKind = noCfg ? "config" : offline ? "offline" : loadError ? "error" : "endpoints";
  const emptyCtx = {
    kind: emptyKind,
    title: loadError ? "Failed to load endpoints" : undefined,
    detail: loadError
      || (offline ? "All target apps are down. Start them with Local Admin enabled." : undefined),
  };

  paintPage(`
    ${connectivityBanner(instanceList)}
    <div class="card">
      <h2>Endpoints <span class="badge">primary unit</span></h2>
      ${!offline && !noCfg ? `
      <p class="muted" style="margin-top:0">Filter: <strong>All</strong> = no filter · explicit selection applies · <strong>None</strong> = empty list.</p>
      <form class="toolbar" id="epFilters">
        <label>Search<input name="search" type="search" value="${esc(search)}" placeholder="route or domain" /></label>
        ${multiSelectHtml("epInst", "Instances", instanceOpts, selInstances)}
        ${multiSelectHtml("epDom", "Domains", domainOpts, selDomains)}
        ${sortSelectHtml("sort", sort, EP_SORT_OPTS)}
        ${applyButtonHtml()}
      </form>` : ""}
      <div id="epTable">
        ${endpointTableHtml(list, emptyCtx)}
        ${list.length ? `
        <div class="pager">
          <button type="button" class="secondary" id="epPrev" ${skip <= 0 ? "disabled" : ""}>Prev</button>
          <span>skip ${skip} · ${list.length} rows</span>
          <button type="button" class="secondary" id="epNext" ${list.length < Number(take) ? "disabled" : ""}>Next</button>
        </div>` : ""}
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
        instances: csvParamFromSelection(readMultiSelect(form, "epInst")),
        domains: csvParamFromSelection(readMultiSelect(form, "epDom")),
        sort: fd.get("sort"),
        take,
        skip: 0,
      });
    });
  }
  const pageParams = () => ({
    search,
    sort,
    take,
    instances: csvParamFromSelection(selInstances),
    domains: csvParamFromSelection(selDomains),
  });
  $("#epPrev")?.addEventListener("click", () => navigate("endpoints", {
    ...pageParams(),
    skip: Math.max(0, skip - Number(take)),
  }));
  $("#epNext")?.addEventListener("click", () => navigate("endpoints", {
    ...pageParams(),
    skip: skip + Number(take),
  }));
}

export async function renderEndpointDetail(routeName, opts = {}) {
  const soft = !!opts.soft;
  setBreadcrumb([
    { label: "Endpoints", href: "#/endpoints" },
    { label: routeName },
  ]);
  beginPageLoad(soft, `<p class="muted">Loading ${esc(routeName)}…</p>`);

  const stats = await api("/api/stats?scope=all&groupByInstance=true");
  let ep = (stats.endpoints || []).find((e) => e.route === routeName);
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
  return `
    <div class="card">
      <h2><code>${esc(ep.route)}</code>
        ${ep.configuredDomain ? `<a class="badge" href="#/domains?name=${encodeURIComponent(ep.configuredDomain)}">${esc(ep.configuredDomain)}</a>` : ""}
        ${hintBadges(ep.hints)}
      </h2>
      <h3 class="section-sub">Recommendations</h3>
      ${hintListHtml(ep.hints)}
      <div class="kpi-row">
        <div class="kpi"><div class="label">Requests</div><div class="value">${num(ep.requests)}</div></div>
        <div class="kpi"><div class="label">OC hit share</div><div class="value">${pct(ep.oc?.hitShare, ep.oc?.lowSample)}</div></div>
        <div class="kpi"><div class="label">Origin share</div><div class="value">${pct(ep.fc?.originShare)}</div></div>
        <div class="kpi"><div class="label">FC stale</div><div class="value">${num(ep.fc?.stale)}</div></div>
      </div>
      <p class="muted">Pipeline</p>
      ${pipelineBar(ep.pipeline, true)}
    </div>
    <div class="detail-grid">
      ${layerDetailOc(ep.oc)}
      ${layerDetailFc(ep.fc)}
    </div>
    ${ep.byInstance?.length ? `
    <div class="card">
      <h2>By instance <span class="badge">spread</span></h2>
      ${ep.instanceSpread ? `<p class="muted">OC hit share ${spreadCell(ep.instanceSpread.ocHitShare)} · Origin ${spreadCell(ep.instanceSpread.originShare)}</p>` : ""}
      <table class="dense">
        <thead><tr><th>Instance</th><th>Req</th><th>OC hit share</th><th>FC hit share</th><th>Origin</th><th>Stale</th><th>Factory</th></tr></thead>
        <tbody>
          ${ep.byInstance.map((bi) => `
            <tr class="clickable" data-id="${esc(bi.instanceId)}">
              <td><code>${esc(bi.instanceId)}</code></td>
              <td>${num(bi.requests)}</td>
              <td>${pct(bi.oc?.hitShare, bi.oc?.lowSample)}</td>
              <td>${pct(bi.fc?.hitShare, bi.fc?.lowSample)}</td>
              <td>${pct(bi.fc?.originShare)}</td>
              <td>${num(bi.fc?.stale)}</td>
              <td>${num(bi.fc?.factoryRuns)}</td>
            </tr>`).join("")}
        </tbody>
      </table>
    </div>` : ""}`;
}

// —— Domains ——

export async function renderDomainsList(params, opts = {}) {
  const soft = !!opts.soft;
  setBreadcrumb([{ label: "Domains", href: "#/domains" }]);
  const search = params.get("search") || "";
  const sort = params.get("sort") || "requests";
  const selInstances = parseCsvParam(params, "instances");

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

  const offline = allInstancesDown(instanceList);
  const noCfg = noInstancesConfigured(instanceList);
  const instanceOpts = (instanceList || []).map((i) => ({ id: i.id, label: i.id }));

  let domains = [];
  let loadError = null;
  if (!noCfg && !offline && !(selInstances !== null && selInstances.length === 0)) {
    try {
      const q = new URLSearchParams({ scope: "all" });
      if (selInstances !== null) q.set("instances", selInstances.join(","));
      const stats = await api("/api/stats?" + q.toString());
      domains = stats.domains || [];
    } catch (err) {
      loadError = err.message;
    }
  }

  domains = sortDomains(filterDomainsBySearch(domains, search), sort);
  const emptyKind = noCfg ? "config" : offline ? "offline" : loadError ? "error" : "domains";

  paintPage(`
    ${connectivityBanner(instanceList)}
    <div class="card">
      <h2>Domains</h2>
      ${!offline && !noCfg ? `
      <form class="toolbar" id="domFilters">
        <label>Search<input name="search" type="search" value="${esc(search)}" placeholder="domain name" /></label>
        ${multiSelectHtml("domInst", "Instances", instanceOpts, selInstances)}
        ${sortSelectHtml("sort", sort, DOMAIN_SORT_OPTS)}
        ${applyButtonHtml()}
      </form>` : ""}
      ${domainTableHtml(domains, {
        kind: emptyKind,
        title: loadError ? "Failed to load domains" : undefined,
        detail: loadError || (offline ? "All target apps are down." : undefined),
      })}
    </div>`, soft);

  bindEmptyStateActions(main());
  const form = $("#domFilters");
  if (form) {
    bindMultiSelects(form);
    form.addEventListener("submit", (ev) => {
      ev.preventDefault();
      const fd = new FormData(form);
      navigate("domains", {
        search: fd.get("search"),
        sort: fd.get("sort"),
        instances: csvParamFromSelection(readMultiSelect(form, "domInst")),
      });
    });
  }
  bindEntityTableClicks(main());
}

export async function renderDomainDetail(name, opts = {}) {
  const soft = !!opts.soft;
  setBreadcrumb([
    { label: "Domains", href: "#/domains" },
    { label: name },
  ]);
  beginPageLoad(soft, `<p class="muted">Loading domain ${esc(name)}…</p>`);

  const [stats, cfgFan] = await Promise.all([
    api("/api/stats?scope=all&groupByInstance=true"),
    api("/api/domains"),
  ]);
  let d = (stats.domains || []).find((x) => x.name === name);
  const cfg = (cfgFan.data || []).find((x) => x.name === name);

  if (!d && !cfg) {
    if (!(soft && mainHasContent())) paintPage(`<div class="card"><p class="status-Down">Domain not found</p></div>`, soft);
    return;
  }

  const domain = d || { name, requests: 0, oc: {}, fc: {}, pipeline: {}, endpoints: [], hints: [] };

  if (soft && $("#domMetricsMount")?.dataset?.metricsReady === "1") {
    const head = $("#domDetailHead");
    if (head) head.innerHTML = domainDetailHeadHtml(name, domain, cfg);
    bindEntityTableClicks(main());
    main().querySelectorAll("tr.clickable[data-id]").forEach((tr) => {
      tr.addEventListener("click", () => navigate("instances", { id: tr.dataset.id }));
    });
    mountDetailMetrics("domMetricsMount", { scope: "domain", domain: name });
    return;
  }

  paintPage(`
    <div id="domDetailHead">${domainDetailHeadHtml(name, domain, cfg)}</div>
    <div id="domMetricsMount"></div>
    <p><a href="#/domains">← Domains</a> · <a href="#/operations?domain=${encodeURIComponent(name)}">Operations</a></p>`, soft);

  main().querySelectorAll("tr.clickable[data-id]").forEach((tr) => {
    tr.addEventListener("click", () => navigate("instances", { id: tr.dataset.id }));
  });
  bindEntityTableClicks(main());
  mountDetailMetrics("domMetricsMount", { scope: "domain", domain: name });
}

function domainDetailHeadHtml(name, domain, cfg) {
  return `
    <div class="card">
      <h2><code>${esc(name)}</code>
        ${domain.versionIsRuntimeOverride ? '<span class="badge">runtime version</span>' : ""}
        ${hintBadges(domain.hints)}
        <a class="badge" href="#/operations?domain=${encodeURIComponent(name)}">Operations</a>
      </h2>
      <h3 class="section-sub">Recommendations</h3>
      ${hintListHtml(domain.hints)}
      <div class="kpi-row">
        <div class="kpi"><div class="label">Version</div><div class="value" style="font-size:1rem">${esc(domain.version || cfg?.version || "—")}</div></div>
        <div class="kpi"><div class="label">Requests</div><div class="value">${num(domain.requests)}</div></div>
        <div class="kpi"><div class="label">OC hit share</div><div class="value">${pct(domain.oc?.hitShare, domain.oc?.lowSample)}</div></div>
        <div class="kpi"><div class="label">Origin share</div><div class="value">${pct(domain.fc?.originShare)}</div></div>
        <div class="kpi"><div class="label">Invalidations</div><div class="value">${num(domain.invalidations)}</div></div>
      </div>
      ${pipelineBar(domain.pipeline, true)}
    </div>
    <div class="detail-grid">
      ${layerDetailOc(domain.oc)}
      ${layerDetailFc(domain.fc)}
      ${cfg ? `
      <div class="detail-block">
        <h3>Effective config</h3>
        <div class="kv">
          <span>Output TTL</span><span>${fmtUnit(cfg.outputCacheTtlSeconds, "s")}</span>
          <span>Fusion soft/hard</span><span>${fmtUnit(cfg.fusionCacheSoftTtlSeconds, "s")} / ${fmtUnit(cfg.fusionCacheHardTtlSeconds, "s")}</span>
          <span>Fail-safe</span><span>${fmtUnit(cfg.fusionCacheFailSafeSeconds, "s")}</span>
          <span>Client TTL / min</span><span>${fmtUnit(cfg.clientTtlSeconds, "s")} / ${fmtUnit(cfg.clientTtlMinSeconds, "s")}</span>
          <span>Schedule phase</span><span>${esc(cfg.schedulePhase || "—")}</span>
          <span>FC instance</span><span>${esc(cfg.fusionCacheInstanceName)}</span>
        </div>
      </div>` : ""}
    </div>
    ${domain.byInstance?.length ? `
    <div class="card">
      <h2>By instance</h2>
      ${domain.instanceSpread ? `<p class="muted">OC hit ${spreadCell(domain.instanceSpread.ocHitShare)} · FC hit ${spreadCell(domain.instanceSpread.fcHitShare)}</p>` : ""}
      <table class="dense">
        <thead><tr><th>Instance</th><th>Version</th><th>Req</th><th>OC hit share</th><th>Origin</th><th>Inv</th></tr></thead>
        <tbody>
          ${domain.byInstance.map((bi) => `
            <tr class="clickable" data-id="${esc(bi.instanceId)}">
              <td><code>${esc(bi.instanceId)}</code></td>
              <td>${esc(bi.version)}${bi.versionIsRuntimeOverride ? " *" : ""}</td>
              <td>${num(bi.requests)}</td>
              <td>${pct(bi.oc?.hitShare, bi.oc?.lowSample)}</td>
              <td>${pct(bi.fc?.originShare)}</td>
              <td>${num(bi.invalidations)}</td>
            </tr>`).join("")}
        </tbody>
      </table>
    </div>` : ""}
    <div class="card">
      <h2>Endpoints in domain</h2>
      ${endpointTableHtml(domain.endpoints || [])}
    </div>`;
}

// —— Instances ——

export async function renderInstancesList(params = new URLSearchParams(), opts = {}) {
  const soft = !!opts.soft;
  setBreadcrumb([{ label: "Instances", href: "#/instances" }]);
  const search = params.get("search") || "";
  const sort = params.get("sort") || "status";

  beginPageLoad(soft, `<div class="card"><p class="muted">Loading instances…</p></div>`);
  let overview;
  try {
    overview = await api("/api/overview");
  } catch (err) {
    if (soft && mainHasContent()) return;
    paintPage(`<div class="card">${emptyStateHtml("error", { detail: err.message })}</div>`, soft);
    bindEmptyStateActions(main());
    return;
  }
  shell.renderHeader(overview);
  shell.updateNavHintsBadge(overview.hintSummary);
  const list = sortInstances(filterInstancesBySearch(overview.instances || [], search), sort);

  paintPage(`
    ${connectivityBanner(overview.instances || [])}
    <div class="card">
      <h2>Instances ${severityStack(overview.hintSummary)}</h2>
      <form class="toolbar" id="instFilters">
        <label>Search<input name="search" type="search" value="${esc(search)}" placeholder="id or url" /></label>
        ${sortSelectHtml("sort", sort, INST_SORT_OPTS)}
        ${applyButtonHtml()}
      </form>
      ${instanceTableHtml(list)}
    </div>`, soft);

  bindEntityTableClicks(main());
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

export async function renderInstanceDetail(id, opts = {}) {
  const soft = !!opts.soft;
  setBreadcrumb([
    { label: "Instances", href: "#/instances" },
    { label: id },
  ]);
  beginPageLoad(soft, `<p class="muted">Loading instance ${esc(id)}…</p>`);

  const [instances, stats] = await Promise.all([
    api("/api/instances"),
    api(`/api/stats?scope=instance:${encodeURIComponent(id)}`),
  ]);
  const inst = instances.find((i) => i.id === id);
  const startedTitle = inst?.startedAtUtc
    ? new Date(inst.startedAtUtc).toISOString()
    : "";

  const st = (inst?.status === 0 || inst?.status === "Healthy") ? "Healthy"
    : (inst?.status === 1 || inst?.status === "Degraded") ? "Degraded"
    : (inst?.status === 2 || inst?.status === "Down") ? "Down"
    : (inst?.status || "unknown");

  if (soft && $("#instMetricsMount")?.dataset?.metricsReady === "1") {
    const head = $("#instDetailHead");
    if (head) head.innerHTML = instanceDetailHeadHtml(id, inst, stats, st, startedTitle);
    bindEntityTableClicks(main());
    mountDetailMetrics("instMetricsMount", { scope: "instance", instanceId: id });
    return;
  }

  paintPage(`
    <div id="instDetailHead">${instanceDetailHeadHtml(id, inst, stats, st, startedTitle)}</div>
    <div id="instMetricsMount"></div>
    <p><a href="#/instances">← Instances</a>
      · <a href="#/operations?target=instance:${encodeURIComponent(id)}">Operations on this instance</a></p>`, soft);

  bindEntityTableClicks(main());
  mountDetailMetrics("instMetricsMount", { scope: "instance", instanceId: id });
}

function instanceDetailHeadHtml(id, inst, stats, st, startedTitle) {
  return `
    <div class="card">
      <h2>Instance <code>${esc(id)}</code>
        <span class="status-${esc(st)}">${esc(st)}</span>
        ${severityStack(inst?.hintSummary)}
      </h2>
      <p class="muted"><code>${esc(inst?.url || "")}</code>
        · reported <code>${esc(inst?.reportedInstanceId || "—")}</code>
        · latency ${formatLatencyMs(inst?.latencyMs)}
        ${inst?.error ? ` · <span class="status-Down">${esc(inst.error)}</span>` : ""}
      </p>
      <div class="kpi-row">
        <div class="kpi" title="${esc(startedTitle)}"><div class="label">Uptime</div><div class="value" style="font-size:1.05rem">${esc(formatUptime(inst?.uptimeSeconds))}</div></div>
        <div class="kpi"><div class="label">Started (UTC)</div><div class="value" style="font-size:0.85rem">${esc(startedTitle ? startedTitle.replace("T", " ").replace(/\.\d+Z$/, "Z") : "—")}</div></div>
        <div class="kpi"><div class="label">Req</div><div class="value">${num(inst?.requests ?? (stats.domains || []).reduce((s, d) => s + (d.requests || 0), 0))}</div></div>
        <div class="kpi"><div class="label">Domains</div><div class="value">${(stats.domains || []).length}</div></div>
        <div class="kpi"><div class="label">Endpoints</div><div class="value">${(stats.endpoints || []).length}</div></div>
      </div>
    </div>
    <div class="card">
      <h2>Domains on instance</h2>
      ${domainTableHtml(stats.domains || [])}
    </div>
    <div class="card">
      <h2>Endpoints on instance</h2>
      ${endpointTableHtml((stats.endpoints || []).slice(0, 50))}
    </div>`;
}

// —— Hints ——

export async function renderHintsPage(params, opts = {}) {
  const soft = !!opts.soft;
  setBreadcrumb([{ label: "Hints" }]);
  const selInstances = parseCsvParam(params, "instances");
  const selDomains = parseCsvParam(params, "domains");
  const selEndpoints = parseCsvParam(params, "endpoints");
  const severity = params.get("severity") || "";

  beginPageLoad(soft, `<div class="card"><p class="muted">Loading hints…</p></div>`);

  const [instanceList, stats] = await Promise.all([
    api("/api/instances"),
    api("/api/stats?scope=all&groupByInstance=true"),
  ]);

  const instanceOpts = (instanceList || []).map((i) => ({ id: i.id, label: i.id }));
  const domainOpts = (stats.domains || []).map((d) => ({ id: d.name, label: d.name }));
  const endpointOpts = (stats.endpoints || []).map((e) => ({ id: e.route, label: e.route }));

  let rows = collectHintRows(stats);
  const totalSummary = summarizeHints(rows);
  // Nav badge always reflects unfiltered cluster totals.
  shell.updateNavHintsBadge(totalSummary);

  const filtersActive = selInstances !== null || selDomains !== null || selEndpoints !== null || !!severity;

  if (selInstances !== null) {
    if (selInstances.length === 0) rows = [];
    else rows = rows.filter((r) => !r.instanceId || selInstances.includes(r.instanceId));
  }
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

  paintPage(`
    <div class="card">
      <h2>Hints ${severityStack(filtersActive ? shownSummary : totalSummary)}
        ${filtersActive ? `<span class="badge muted" title="Visible / all">${shownSummary.total}/${totalSummary.total}</span>` : ""}
      </h2>
      <p class="muted">Rule-based recommendations from live stats. Filters combine (AND). Empty hint mark is <strong>○</strong>.
        ${filtersActive ? " Severity KPIs show <strong>visible/total</strong> for the current filter." : ""}
      </p>
      <form class="toolbar" id="hintFilters">
        ${multiSelectHtml("hInst", "Instances", instanceOpts, selInstances)}
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
      <div class="kpi-row">
        <div class="kpi"><div class="label">Critical</div><div class="value status-Down">${ratio(shownSummary.critical, totalSummary.critical)}</div></div>
        <div class="kpi"><div class="label">Warning</div><div class="value" style="color:var(--warn)">${ratio(shownSummary.warning, totalSummary.warning)}</div></div>
        <div class="kpi"><div class="label">Info</div><div class="value" style="color:var(--accent)">${ratio(shownSummary.info, totalSummary.info)}</div></div>
        <div class="kpi"><div class="label">Shown</div><div class="value">${ratio(shownSummary.total, totalSummary.total)}</div></div>
      </div>
      ${rows.length ? `
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
      </table>` : emptyStateHtml("filter", {
        title: "No hints to show",
        detail: "No recommendations from live data for the current filters. Generate traffic on healthy apps or clear filters.",
      })}
    </div>`, soft);
  bindEmptyStateActions(main());

  const form = $("#hintFilters");
  bindMultiSelects(form);
  form.addEventListener("submit", (ev) => {
    ev.preventDefault();
    const fd = new FormData(form);
    navigate("hints", {
      instances: csvParamFromSelection(readMultiSelect(form, "hInst")),
      domains: csvParamFromSelection(readMultiSelect(form, "hDom")),
      endpoints: csvParamFromSelection(readMultiSelect(form, "hEp")),
      severity: fd.get("severity") || "",
    });
  });
}

// —— Operations ——

export async function renderOperations(params) {
  setBreadcrumb([{ label: "Operations" }]);
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
  const modeLabel = mode === "bus-distribute" ? "Cluster bus (distribute)" : "HTTP fan-out";
  const modeDetail = distribution?.summary
    || "Probe /api/distribution for live capability.";

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
          <strong>fan-out</strong> = Admin App calls every target with <code>distribute:false</code> (each node applies locally).
          <strong>bus-distribute</strong> = one origin with <code>distribute:true</code>; peers apply via CacheOrchestrator.Bus (never both).
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
      <p class="muted">From Local Admin <code>GET …/cluster/info</code> on each configured instance.</p>
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
      ? `<span class="badge ok">bus-distribute</span>`
      : `<span class="badge warn">fan-out</span>`;
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
          if (name) await renderDomainDetail(name, { soft });
          else await renderDomainsList(params, { soft });
        } else if (root === "instances") {
          const id = params.get("id");
          if (id) await renderInstanceDetail(id, { soft });
          else await renderInstancesList(params, { soft });
        } else if (root === "operations") {
          await renderOperations(params);
        } else if (root === "hints") {
          await renderHintsPage(params, { soft });
        } else if (root === "metrics") {
          await renderMetrics(params, { soft });
        } else if (!soft) {
          navigate("overview");
        }
      })(),
    ]);
  } catch (err) {
    // Browser console only — Admin App process logs do not capture SPA errors.
    console.error("[Admin UI] route failed", err);
    if (soft && mainHasContent()) return;
    main().innerHTML = `<div class="card">${emptyStateHtml("error", {
      title: "Page failed to load",
      detail: err?.message || String(err),
    })}</div>`;
    bindEmptyStateActions(main());
  }
}
