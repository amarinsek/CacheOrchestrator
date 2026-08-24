/**
 * Domains list + detail pages.
 */

import { api } from "./api.js";
import { $, beginPageLoad, main, mainHasContent, paintPage } from "./dom.js";
import {
  currentValueHtml,
  esc,
  fafcHtml,
  factoryShareOf,
  fmtDurationMs,
  fmtUnit,
  impactBandLabel,
  METRIC_TITLES,
  num,
  pct,
  pipelineBar,
  pipelinePanelHtml,
  staleShareHtml,
  thMetric,
  tipAttr,
} from "./format.js";
import {
  applyButtonHtml,
  DOMAIN_SORT_OPTS,
  EP_SORT_OPTS,
  filterDomainsBySearch,
  inlineSortSelectHtml,
  sortDomains,
  sortEndpoints,
  sortSelectHtml,
} from "./filters.js";
import { hintBadges, recommendationsSectionHtml } from "./hints.js";
import { navigate, setBreadcrumb } from "./router.js";
import {
  bindEmptyStateActions,
  bindEntityTableClicks,
  connectivityBanner,
  domainTableHtml,
  emptyStateHtml,
  endpointTableHtml,
  impactDetailHtml,
  fadCell,
  impactKpiRowHtml,
  layerDetailFc,
  layerDetailOc,
} from "./tables.js";
import { mountDetailMetrics } from "./views-metrics.js";
import { fetchWindowStatsIfNeeded, metricsRequiredEmpty } from "./views-shared.js";

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
  setBreadcrumb([]);
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

  const domain = d || { name, requests: 0, outputCache: {}, dataCache: {}, pipeline: {}, endpoints: [], hints: [] };
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
    <div id="domMetricsMount"></div>`, soft);

  main().querySelectorAll("tr.clickable[data-id]").forEach((tr) => {
    tr.addEventListener("click", () => navigate("instances", { id: tr.dataset.id }));
  });
  bindEntityTableClicks(main());
  $("#domEpSort")?.addEventListener("change", (ev) => {
    navigate("domains", { name, epSort: ev.target.value });
  });
  mountDetailMetrics("domMetricsMount", { scope: "domain", domain: name });
}

export function domainDetailHeadHtml(name, domain, cfg) {
  const ver = domain.version || cfg?.version || "—";
  const clientTtl = `${fmtUnit(cfg?.clientTtlSeconds, "s")} / ${fmtUnit(cfg?.clientTtlMinSeconds, "s")}`;
  const hasConfig = !!(cfg || (ver && ver !== "—"));
  const verRt = !!(domain.versionIsRuntimeOverride || cfg?.versionIsRuntimeOverride);
  return `
    <div class="card">
      <h2>Domain <code>${esc(name)}</code>
        ${verRt ? '<span class="badge">runtime version</span>' : ""}
        ${hintBadges(domain.hints)}
        <a class="badge" href="#/operations?domain=${encodeURIComponent(name)}">Operations</a>
      </h2>
      ${recommendationsSectionHtml(domain.hints)}
      <div class="kpi-row">
        <div class="kpi"${tipAttr("inv")}><div class="label">Inv</div><div class="value">${num(domain.invalidations)}</div></div>
        <div class="kpi"${tipAttr("req")}><div class="label">Req</div><div class="value">${num(domain.requests)}</div></div>
        ${impactKpiRowHtml(domain.impact, domain.dataCache)}
      </div>
      ${pipelinePanelHtml(domain.pipeline)}
    </div>
    <div class="detail-grid">
      ${layerDetailOc(domain.outputCache)}
      ${layerDetailFc(domain.dataCache)}
      ${impactDetailHtml(domain.impact)}
      ${hasConfig ? `
      <div class="detail-block">
        <h3>Effective config <span class="badge" title="Current - Values not part of the selected time range">current</span></h3>
        <div class="kv">
          <span${tipAttr("version")}>Version</span>${currentValueHtml(esc(ver))}${verRt ? " *" : ""}
          ${cfg ? `
          <span${tipAttr("outputTtl")}>Output TTL</span>${currentValueHtml(fmtUnit(cfg.outputCacheTtlSeconds, "s"))}
          <span${tipAttr("softTtl")}>Data soft TTL</span>${currentValueHtml(fmtUnit(cfg.dataCacheTtlSeconds, "s"))}
          <span${tipAttr("hardTtl")}>Data hard TTL</span>${currentValueHtml(fmtUnit(cfg.hardTtlSeconds, "s"))}
          <span${tipAttr("failSafe")}>Fail-safe</span>${currentValueHtml(fmtUnit(cfg.failSafeSeconds, "s"))}
          <span${tipAttr("clientTtl")}>Client TTL / min</span>${currentValueHtml(clientTtl)}
          <span${tipAttr("schedulePhase")}>Schedule phase</span>${currentValueHtml(esc(cfg.schedulePhase || "—"))}
          <span${tipAttr("dcInstance")}>DC instance</span>${currentValueHtml(esc(cfg.dataCacheInstanceName || "—"))}
          ` : ""}
        </div>
      </div>` : ""}
    </div>
    ${domain.byInstance?.length ? `
    <div class="card">
      <h2>By instance</h2>
      <table class="dense entity-table">
        <thead><tr>
          ${thMetric("Instance", "instance", { fromKey: true })}
          ${thMetric("Version", "version", { fromKey: true })}
          ${thMetric("Req", "req", { fromKey: true })}
          ${thMetric("Inv", "inv", { fromKey: true })}
          ${thMetric("Pipeline", "pipeline", { fromKey: true })}
          ${thMetric("OC hit %", "outputCacheHitShare", { fromKey: true })}
          ${thMetric("DC hit %", "dataCacheHitShare", { fromKey: true })}
          ${thMetric("FA run %", "factoryShare", { fromKey: true })}
          ${thMetric("FAFC", "factoryFailures", { fromKey: true, className: "col-num" })}
          ${thMetric("FAD", "avgFactoryDuration", { fromKey: true })}
          ${thMetric("DC stale %", "staleShare", { fromKey: true })}
          ${thMetric("EFTS", "estTimeSaved", { fromKey: true })}
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
              <td class="col-pipe">${pipelineBar(bi.pipeline, false, { title: false, segmentTips: false })}</td>
              <td>${pct(bi.outputCache?.hitShare, bi.outputCache?.lowRequestSample, "request")}</td>
              <td>${pct(bi.dataCache?.hitShare, bi.dataCache?.lowRequestSample, "request")}</td>
              <td>${pct(factoryShareOf(bi.dataCache), bi.dataCache?.lowRequestSample, "request")}</td>
              ${fafcHtml(bi.dataCache)}
              <td>${fadCell(bi.impact)}</td>
              ${staleShareHtml(bi.dataCache)}
              <td>${fmtDurationMs(bi.impact?.estFactoryTimeSavedMs)}</td>
              <td>${impactBandLabel(bi.impact?.benefit, { html: true })}</td>
              <td>${impactBandLabel(bi.impact?.candidate, { html: true })}</td>
            </tr>`).join("")}
        </tbody>
      </table>
    </div>` : ""}`;
}
