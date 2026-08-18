/**
 * Hints page — recommendation table for the selected time range.
 */

import { $, beginPageLoad, main, paintPage } from "./dom.js";
import {
  applyButtonHtml,
  bindMultiSelects,
  csvParamFromSelection,
  multiSelectHtml,
  parseCsvParam,
  readMultiSelect,
} from "./filters.js";
import { esc } from "./format.js";
import {
  collectHintRows,
  severityStack,
  summarizeHints,
} from "./hints.js";
import { navigate, setBreadcrumb } from "./router.js";
import * as shell from "./shell.js";
import { bindEmptyStateActions, emptyStateHtml } from "./tables.js";
import { fetchWindowStatsIfNeeded, metricsRequiredEmpty } from "./views-shared.js";

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
