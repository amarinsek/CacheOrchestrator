/**
 * Endpoints list + detail pages.
 */

import { api } from "./api.js";
import { $, beginPageLoad, main, mainHasContent, paintPage } from "./dom.js";
import {
  currentValueHtml,
  esc,
  factoryShareOf,
  fmtDurationMs,
  impactBandLabel,
  METRIC_TITLES,
  num,
  pct,
  pipelineBar,
  thMetric,
  tipAttr,
} from "./format.js";
import {
  applyButtonHtml,
  bindMultiSelects,
  csvParamFromSelection,
  EP_SORT_OPTS,
  multiSelectHtml,
  parseCsvParam,
  readMultiSelect,
  sortEndpoints,
  sortSelectHtml,
} from "./filters.js";
import { hintBadges, recommendationsSectionHtml } from "./hints.js";
import { navigate, setBreadcrumb } from "./router.js";
import {
  bindEmptyStateActions,
  bindEntityTableClicks,
  connectivityBanner,
  emptyStateHtml,
  endpointTableHtml,
  impactDetailHtml,
  impactKpiRowHtml,
  layerDetailFc,
  layerDetailOc,
} from "./tables.js";
import { mountDetailMetrics } from "./views-metrics.js";
import { fetchWindowStatsIfNeeded, metricsRequiredEmpty } from "./views-shared.js";

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

export function endpointDetailHeadHtml(ep) {
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
