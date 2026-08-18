/**
 * Live page — near-real-time health & performance (fixed 1m Prometheus lookback).
 * Same visual components as Overview / Domains / Endpoints; independent of global Range.
 */

import { api } from "./api.js";
import { $, beginPageLoad, kpiRowHtml, main, mainHasContent, paintPage } from "./dom.js";
import {
  esc,
  fmtRequestRate,
  noDataHtml,
  pipelinePanelHtml,
  tipAttr,
} from "./format.js";
import {
  applyButtonHtml,
  bindMultiSelects,
  csvParamFromSelection,
  DOMAIN_SORT_OPTS,
  EP_SORT_OPTS,
  filterDomainsBySearch,
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
import { severityStack } from "./hints.js";
import { navigate, setBreadcrumb, setNavActive } from "./router.js";
import {
  bindEmptyStateActions,
  bindEntityTableClicks,
  connectivityBanner,
  domainTableHtml,
  emptyStateHtml,
  endpointTableHtml,
  instanceTableHtml,
} from "./tables.js";
import * as shell from "./shell.js";
import { bindGotoHints, instancesUpClass } from "./views-shared.js";

function shareOrDash(v) {
  if (v == null) return noDataHtml("No samples yet");
  const n = Number(v);
  if (!Number.isFinite(n)) return noDataHtml();
  return `${(n * 100).toFixed(1)}%`;
}

function rateOrDash(v) {
  return v == null ? noDataHtml("No samples yet") : fmtRequestRate(v);
}

/**
 * @param {URLSearchParams} [params]
 * @param {{ soft?: boolean }} [opts]
 */
export async function renderLive(params = new URLSearchParams(), opts = {}) {
  const soft = !!opts.soft;
  setBreadcrumb([]);
  setNavActive("live");

  const instSort = params.get("instSort") || "status";
  const domSearch = params.get("domSearch") || "";
  const domSort = params.get("domSort") || "peakRequestRate";
  const epSearch = params.get("epSearch") || "";
  const epSort = params.get("epSort") || "peakRequestRate";
  const selDomains = parseCsvParam(params, "epDomains");
  const quietSearch = params.get("quietSearch") || "";
  const quietSort = params.get("quietSort") || "name";

  beginPageLoad(soft, `<div class="card"><p class="muted">Loading live snapshot…</p></div>`);

  let snap;
  try {
    snap = await api("/api/live");
  } catch (err) {
    if (soft && mainHasContent()) return;
    paintPage(`<div class="card">${emptyStateHtml("error", { detail: err.message })}</div>`, soft);
    bindEmptyStateActions(main());
    return;
  }

  shell.updateNavHintsBadge(snap.hintSummary);

  const c = snap.cluster || {};
  const lookback = snap.lookback || "1m";
  const metricsOk = snap.status === "Connected";
  const instances = sortInstances(snap.instances || [], instSort);

  let hotDomains = sortDomains(
    filterDomainsBySearch(snap.domains || [], domSearch),
    domSort);

  let hotEndpoints = [...(snap.endpoints || [])];
  if (selDomains !== null) {
    if (selDomains.length === 0) hotEndpoints = [];
    else {
      hotEndpoints = hotEndpoints.filter((e) =>
        selDomains.includes(e.configuredDomain || e.domain || ""));
    }
  }
  if (epSearch) {
    const q = epSearch.toLowerCase();
    hotEndpoints = hotEndpoints.filter((e) =>
      (e.route || "").toLowerCase().includes(q)
      || (e.configuredDomain || "").toLowerCase().includes(q));
  }
  hotEndpoints = sortEndpoints(hotEndpoints, epSort);

  let quietDomains = sortDomains(
    filterDomainsBySearch(snap.quietDomains || [], quietSearch),
    quietSort);

  const domainOpts = [
    ...new Set([
      ...(snap.domains || []).map((d) => d.name).filter(Boolean),
      ...(snap.endpoints || []).map((e) => e.configuredDomain).filter(Boolean),
    ]),
  ].sort((a, b) => a.localeCompare(b)).map((n) => ({ id: n, label: n }));

  const upClass = instancesUpClass({
    healthyCount: c.healthyCount,
    degradedCount: c.degradedCount,
    downCount: c.downCount,
    instances,
  });

  const kpis = kpiRowHtml([
    {
      label: "Instances up",
      valueHtml: `${c.healthyCount ?? 0} / ${c.instanceCount ?? (instances.length || 0)}`,
      valueClass: upClass,
    },
    {
      label: "RPS",
      valueHtml: metricsOk ? rateOrDash(c.requestRate) : noDataHtml("Metrics offline"),
      tipAttr: tipAttr("liveRps"),
    },
    {
      label: "Factory / s",
      valueHtml: metricsOk ? rateOrDash(c.factoryRate) : noDataHtml(),
      tipAttr: tipAttr("factoryRate"),
    },
    {
      label: "Inv / s",
      valueHtml: metricsOk ? rateOrDash(c.invalidationRate) : noDataHtml(),
      tipAttr: tipAttr("invRate"),
    }
  ], "liveKpis");

  const headHtml = `
      <div class="card-head">
        <h2>Live <span class="badge ok" title="Current values over the last minute">last ${esc(lookback)}</span></h2>
        <span class="muted small">${snap.queriedAtUtc ? new Date(snap.queriedAtUtc).toISOString().replace("T", " ").replace(/\.\d+Z$/, "Z") : ""}</span>
      </div>
      <p class="muted" style="margin:0 0 0.75rem">Current health and performance (fixed lookback — not the global Range picker).</p>
      ${kpis}
      ${!metricsOk ? `<p class="status-Degraded" style="margin:0.75rem 0 0">${esc(snap.error || "Connect metrics to see live rates.")}</p>` : ""}
  `;

  const pipeHtml = pipelinePanelHtml(metricsOk ? snap.pipeline : null);
  const instHtml = instanceTableHtml(instances, { kind: "config" });
  const domHtml = !metricsOk
    ? emptyStateHtml("metrics-config", { title: "Metrics not connected", detail: snap.error })
    : domainTableHtml(hotDomains, {
      kind: "domains",
      title: hotDomains.length ? undefined : "No hot domains",
      detail: hotDomains.length ? undefined : "No domain traffic in the live lookback.",
    });
  const epHtml = !metricsOk
    ? emptyStateHtml("metrics-config", { title: "Metrics not connected", detail: snap.error })
    : endpointTableHtml(hotEndpoints, {
      kind: "endpoints",
      title: hotEndpoints.length ? undefined : "No hot endpoints",
      detail: hotEndpoints.length ? undefined : "No endpoint traffic in the live lookback.",
    });
  const quietHtml = !metricsOk
    ? ""
    : domainTableHtml(quietDomains, {
      kind: "domains",
      title: quietDomains.length ? undefined : "No quiet domains",
      detail: quietDomains.length
        ? undefined
        : "Every configured domain has traffic in the live lookback.",
    });

  const liveParams = (patch) => ({
    instSort,
    domSearch,
    domSort,
    epSearch,
    epSort,
    epDomains: csvParamFromSelection(selDomains),
    quietSearch,
    quietSort,
    ...patch,
  });

  const bindLiveForms = () => {
    $("#liveInstSort")?.addEventListener("change", (ev) => {
      navigate("live", liveParams({ instSort: ev.target.value }));
    });
    const domForm = $("#liveDomFilters");
    if (domForm) {
      domForm.addEventListener("submit", (ev) => {
        ev.preventDefault();
        const fd = new FormData(domForm);
        navigate("live", liveParams({
          domSearch: fd.get("domSearch") || "",
          domSort: fd.get("domSort") || "peakRequestRate",
        }));
      });
    }
    const epForm = $("#liveEpFilters");
    if (epForm) {
      bindMultiSelects(epForm);
      epForm.addEventListener("submit", (ev) => {
        ev.preventDefault();
        const fd = new FormData(epForm);
        navigate("live", liveParams({
          epSearch: fd.get("epSearch") || "",
          epSort: fd.get("epSort") || "peakRequestRate",
          epDomains: csvParamFromSelection(readMultiSelect(epForm, "epDomains")),
        }));
      });
    }
    const quietForm = $("#liveQuietFilters");
    if (quietForm) {
      quietForm.addEventListener("submit", (ev) => {
        ev.preventDefault();
        const fd = new FormData(quietForm);
        navigate("live", liveParams({
          quietSearch: fd.get("quietSearch") || "",
          quietSort: fd.get("quietSort") || "name",
        }));
      });
    }
  };

  if (soft && document.getElementById("liveRoot")) {
    const banner = $("#liveBanner");
    if (banner) banner.innerHTML = connectivityBanner(instances);
    const head = document.getElementById("liveHeadCard");
    if (head) head.innerHTML = headHtml;
    const pipe = $("#livePipeline");
    if (pipe) pipe.innerHTML = pipeHtml;
    const inst = document.getElementById("liveInstTable");
    if (inst) inst.innerHTML = instHtml;
    const dom = document.getElementById("liveDomTable");
    if (dom) dom.innerHTML = domHtml;
    const ep = document.getElementById("liveEpTable");
    if (ep) ep.innerHTML = epHtml;
    const quiet = document.getElementById("liveQuietTable");
    if (quiet) quiet.innerHTML = quietHtml;
    bindEmptyStateActions(main());
    bindEntityTableClicks(main());
    bindGotoHints(main());
    return;
  }

  paintPage(`
    <div id="liveRoot">
    <div id="liveBanner">${connectivityBanner(instances)}</div>
    <div class="card" id="liveHeadCard">${headHtml}</div>
    <div class="card" id="livePipeline">${pipeHtml}</div>

    <div class="card">
      <div class="card-head">
        <h2>Instances</h2>
        ${inlineSortSelectHtml("liveInstSort", instSort, INST_SORT_OPTS)}
      </div>
      <div id="liveInstTable">${instHtml}</div>
    </div>

    <div class="card">
      <div class="card-head">
        <h2>Hot domains <span class="badge">live RPS &gt; 0</span></h2>
      </div>
      ${metricsOk ? `
      <form class="toolbar" id="liveDomFilters">
        <label>Search<input name="domSearch" type="search" value="${esc(domSearch)}" placeholder="domain name" /></label>
        ${sortSelectHtml("domSort", domSort, DOMAIN_SORT_OPTS)}
        ${applyButtonHtml()}
      </form>` : ""}
      <div id="liveDomTable">${domHtml}</div>
    </div>

    <div class="card">
      <div class="card-head">
        <h2>Hot endpoints <span class="badge">live RPS &gt; 0</span></h2>
      </div>
      ${metricsOk ? `
      <form class="toolbar" id="liveEpFilters">
        <label>Search<input name="epSearch" type="search" value="${esc(epSearch)}" placeholder="route or domain" /></label>
        ${multiSelectHtml("epDomains", "Domains", domainOpts, selDomains)}
        ${sortSelectHtml("epSort", epSort, EP_SORT_OPTS)}
        ${applyButtonHtml()}
      </form>` : ""}
      <div id="liveEpTable">${epHtml}</div>
    </div>

    <div class="card">
      <div class="card-head">
        <h2>Quiet domains <span class="badge muted">RPS ≈ 0</span></h2>
      </div>
      <p class="muted" style="margin:0 0 0.5rem">Configured domains with no traffic in the last ${esc(lookback)}.</p>
      ${metricsOk ? `
      <form class="toolbar" id="liveQuietFilters">
        <label>Search<input name="quietSearch" type="search" value="${esc(quietSearch)}" placeholder="domain name" /></label>
        ${sortSelectHtml("quietSort", quietSort, DOMAIN_SORT_OPTS)}
        ${applyButtonHtml()}
      </form>` : ""}
      <div id="liveQuietTable">${quietHtml || emptyStateHtml("metrics-config", { detail: snap.error })}</div>
    </div>

    <p class="muted small"><a href="#/metrics">Metrics</a> for history · <a href="#/overview">Overview</a> for the selected time range</p>
    </div>
  `, soft);

  bindEmptyStateActions(main());
  bindEntityTableClicks(main());
  bindGotoHints(main());
  bindLiveForms();
}
