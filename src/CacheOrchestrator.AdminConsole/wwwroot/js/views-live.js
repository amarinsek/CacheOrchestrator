/**
 * Live page — near-real-time health & performance (fixed 1m Prometheus lookback).
 * Independent of the global Range picker.
 */

import { api, instanceStatus } from "./api.js";
import { beginPageLoad, kpiRowHtml, main, mainHasContent, paintPage } from "./dom.js";
import {
  esc,
  formatLatencyMs,
  formatUptime,
  fmtRequestRate,
  METRIC_TITLES,
  noDataHtml,
  pct,
  tipAttr,
} from "./format.js";
import { severityStack } from "./hints.js";
import { setBreadcrumb, setNavActive } from "./router.js";
import { bindEmptyStateActions, bindEntityTableClicks, emptyStateHtml } from "./tables.js";
import * as shell from "./shell.js";
import { bindGotoHints } from "./views-shared.js";

function shareOrDash(v) {
  return v == null ? noDataHtml("No samples yet") : pct(v);
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
  const metricsLine = snap.metrics
    ? `${esc(snap.metrics.provider || "Prometheus")}${snap.metrics.host ? ` · ${esc(snap.metrics.host)}` : ""} · ${esc(snap.metrics.status || "")}`
    : "";

  const upClass = c.downCount > 0 || c.healthyCount < (c.instanceCount || 0)
    ? "status-Down"
    : c.degradedCount > 0
      ? "status-Degraded"
      : "status-Healthy";

  const kpis = kpiRowHtml([
    {
      label: "Instances up",
      valueHtml: `${c.healthyCount ?? 0} / ${c.instanceCount ?? 0}`,
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
      title: "Factory (FC miss) rate",
    },
    {
      label: "Inv / s",
      valueHtml: metricsOk ? rateOrDash(c.invalidationRate) : noDataHtml(),
      title: "Invalidation rate",
    },
    {
      label: "OC hit %",
      valueHtml: metricsOk ? shareOrDash(c.ocHitShare) : noDataHtml(),
      tipAttr: tipAttr("ocHitShare"),
    },
    {
      label: "FC hit %",
      valueHtml: metricsOk ? shareOrDash(c.fcHitShare) : noDataHtml(),
      tipAttr: tipAttr("fcHitShare"),
    },
    {
      label: "Factory %",
      valueHtml: metricsOk ? shareOrDash(c.factoryShare) : noDataHtml(),
      tipAttr: tipAttr("factoryShare"),
    },
    {
      label: "Fail %",
      valueHtml: metricsOk ? shareOrDash(c.factoryFailShare) : noDataHtml(),
      title: "FC fail + stale share of requests",
    },
    {
      label: "Hints",
      valueHtml: severityStack(snap.hintSummary),
      className: "kpi-hints",
      attrs: 'role="link" tabindex="0" data-goto-hints="1"',
      title: "Open Hints",
    },
  ], "liveKpis");

  const bodyHtml = `
      <div class="card-head">
        <h2>Live <span class="badge ok" title="Current values over the last minute">last ${esc(lookback)}</span></h2>
        <span class="muted small">${snap.queriedAtUtc ? new Date(snap.queriedAtUtc).toISOString().replace("T", " ").replace(/\.\d+Z$/, "Z") : ""}</span>
      </div>
      <p class="muted" style="margin:0 0 0.75rem">Current health and performance.</p>
      ${kpis}
      ${!metricsOk ? `<p class="status-Degraded" style="margin:0.75rem 0 0">${esc(snap.error || "Connect metrics to see live rates.")}</p>` : ""}
      ${metricsLine ? `<p class="muted small" style="margin:0.5rem 0 0">${metricsLine}</p>` : ""}
  `;

  const instHtml = liveInstancesTable(snap.instances || []);
  const domHtml = !metricsOk
    ? emptyStateHtml("metrics-config", { title: "Metrics not connected", detail: snap.error })
    : liveEntityTable(snap.domains || [], { kind: "domain" });
  const epHtml = !metricsOk
    ? emptyStateHtml("metrics-config", { title: "Metrics not connected", detail: snap.error })
    : liveEntityTable(snap.endpoints || [], { kind: "endpoint" });
  const quietHtml = (snap.quietDomains || []).length ? `
    <div class="card" id="liveQuietCard">
      <h2>Quiet domains <span class="badge muted">RPS ≈ 0</span></h2>
      <p class="muted" style="margin:0 0 0.5rem">Configured domains with no traffic in the last ${esc(lookback)}.</p>
      <p style="margin:0">${snap.quietDomains.map((n) =>
        `<a class="badge" href="#/domains?name=${encodeURIComponent(n)}"><code>${esc(n)}</code></a>`).join(" ")}</p>
    </div>` : "";

  if (soft && document.getElementById("liveRoot")) {
    const head = document.getElementById("liveHeadCard");
    if (head) head.innerHTML = bodyHtml;
    const inst = document.getElementById("liveInstTable");
    if (inst) inst.innerHTML = instHtml;
    const dom = document.getElementById("liveDomTable");
    if (dom) dom.innerHTML = domHtml;
    const ep = document.getElementById("liveEpTable");
    if (ep) ep.innerHTML = epHtml;
    const quietHost = document.getElementById("liveQuietHost");
    if (quietHost) quietHost.innerHTML = quietHtml;
    bindEmptyStateActions(main());
    bindEntityTableClicks(main());
    bindGotoHints(main());
    return;
  }

  paintPage(`
    <div id="liveRoot">
    <div class="card" id="liveHeadCard">${bodyHtml}</div>

    <div class="card">
      <h2>Instances</h2>
      <div id="liveInstTable">${instHtml}</div>
    </div>

    <div class="card">
      <h2>Hot domains <span class="badge">by RPS</span></h2>
      <div id="liveDomTable">${domHtml}</div>
    </div>

    <div class="card">
      <h2>Hot endpoints <span class="badge">top by RPS</span></h2>
      <div id="liveEpTable">${epHtml}</div>
    </div>

    <div id="liveQuietHost">${quietHtml}</div>

    <p class="muted small"><a href="#/metrics">Metrics</a> for history · <a href="#/overview">Overview</a> for the selected time range</p>
    </div>
  `, soft);

  bindEmptyStateActions(main());
  bindEntityTableClicks(main());
  bindGotoHints(main());
}

function liveInstancesTable(list) {
  if (!list.length) {
    return emptyStateHtml("config", {
      title: "No instances configured",
      detail: "Add targets under AdminConsole:Instances.",
    });
  }
  return `
    <table class="dense entity-table">
      <thead>
        <tr>
          <th>Id</th>
          <th>Status</th>
          <th class="col-num" title="${esc(METRIC_TITLES.liveRps || "Request rate")}">RPS</th>
          <th class="col-num">Latency</th>
          <th>Uptime</th>
          <th>Error</th>
        </tr>
      </thead>
      <tbody>
        ${list.map((i) => {
          const st = instanceStatus(i.status);
          return `
          <tr class="clickable entity-row" data-entity="instance" data-id="${esc(i.id)}">
            <td class="col-name"><code>${esc(i.id)}</code></td>
            <td><span class="status-${esc(st)}">${esc(st)}</span></td>
            <td class="col-num">${i.requestRate != null ? fmtRequestRate(i.requestRate) : "—"}</td>
            <td class="col-num">${formatLatencyMs(i.latencyMs)}</td>
            <td>${formatUptime(i.uptimeSeconds)}</td>
            <td class="muted">${esc(i.error || "")}</td>
          </tr>`;
        }).join("")}
      </tbody>
    </table>`;
}

function liveEntityTable(list, { kind }) {
  if (!list.length) {
    return emptyStateHtml(kind === "endpoint" ? "endpoints" : "domains", {
      detail: "No traffic in the live lookback.",
    });
  }
  const nameHeader = kind === "endpoint" ? "Route" : "Domain";
  return `
    <table class="dense entity-table">
      <thead>
        <tr>
          <th>${nameHeader}</th>
          ${kind === "endpoint" ? "<th>Domain</th>" : ""}
          <th class="col-num">RPS</th>
          <th class="col-num">OC hit %</th>
          <th class="col-num">FC hit %</th>
          <th class="col-num">Factory %</th>
          <th class="col-num">Fail %</th>
        </tr>
      </thead>
      <tbody>
        ${list.map((e) => {
          const name = e.name || "";
          const entity = kind === "endpoint" ? "endpoint" : "domain";
          const dataAttr = kind === "endpoint"
            ? `data-entity="endpoint" data-route="${esc(name)}"`
            : `data-entity="domain" data-name="${esc(name)}"`;
          return `
          <tr class="clickable entity-row" ${dataAttr}>
            <td class="col-name"><code>${esc(name)}</code></td>
            ${kind === "endpoint" ? `<td>${esc(e.domain || "—")}</td>` : ""}
            <td class="col-num">${fmtRequestRate(e.requestRate)}</td>
            <td class="col-num">${shareOrDash(e.ocHitShare)}</td>
            <td class="col-num">${shareOrDash(e.fcHitShare)}</td>
            <td class="col-num">${shareOrDash(e.factoryShare)}</td>
            <td class="col-num">${shareOrDash(e.factoryFailShare)}</td>
          </tr>`;
        }).join("")}
      </tbody>
    </table>`;
}
