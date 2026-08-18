/**
 * Live page — near-real-time health & performance (fixed 1m Prometheus lookback).
 * Independent of the global Range picker.
 */

import { api, instanceStatus } from "./api.js";
import { main, mainHasContent, paintMain } from "./dom.js";
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
import { bindEmptyStateActions, emptyStateHtml } from "./tables.js";
import * as shell from "./shell.js";

function beginPageLoad(soft, loadingHtml) {
  if (!soft || !mainHasContent()) {
    main().innerHTML = loadingHtml;
  }
}

function paintPage(html, soft) {
  if (soft) paintMain(html);
  else main().innerHTML = html;
}

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

  paintPage(`
    <div class="card">
      <div class="card-head">
        <h2>Live <span class="badge ok" title="Current values over the last minute">last ${esc(lookback)}</span></h2>
        <span class="muted small">${snap.queriedAtUtc ? new Date(snap.queriedAtUtc).toISOString().replace("T", " ").replace(/\.\d+Z$/, "Z") : ""}</span>
      </div>
      <p class="muted" style="margin:0 0 0.75rem">Current health and performance.</p>
      <div class="kpi-row">
        <div class="kpi"><div class="label">Instances up</div><div class="value ${upClass}">${c.healthyCount ?? 0} / ${c.instanceCount ?? 0}</div></div>
        <div class="kpi"${tipAttr("liveRps")}><div class="label">RPS</div><div class="value">${metricsOk ? rateOrDash(c.requestRate) : noDataHtml("Metrics offline")}</div></div>
        <div class="kpi" title="Factory (FC miss) rate"><div class="label">Factory / s</div><div class="value">${metricsOk ? rateOrDash(c.factoryRate) : noDataHtml()}</div></div>
        <div class="kpi" title="Invalidation rate"><div class="label">Inv / s</div><div class="value">${metricsOk ? rateOrDash(c.invalidationRate) : noDataHtml()}</div></div>
        <div class="kpi"${tipAttr("ocHitShare")}><div class="label">OC hit %</div><div class="value">${metricsOk ? shareOrDash(c.ocHitShare) : noDataHtml()}</div></div>
        <div class="kpi"${tipAttr("fcHitShare")}><div class="label">FC hit %</div><div class="value">${metricsOk ? shareOrDash(c.fcHitShare) : noDataHtml()}</div></div>
        <div class="kpi"${tipAttr("factoryShare")}><div class="label">Factory %</div><div class="value">${metricsOk ? shareOrDash(c.factoryShare) : noDataHtml()}</div></div>
        <div class="kpi" title="FC fail + stale share of requests"><div class="label">Fail %</div><div class="value">${metricsOk ? shareOrDash(c.factoryFailShare) : noDataHtml()}</div></div>
        <div class="kpi kpi-hints" role="link" tabindex="0" data-goto-hints="1" title="Open Hints"><div class="label">Hints</div><div class="value">${severityStack(snap.hintSummary)}</div></div>
      </div>
      ${!metricsOk ? `<p class="status-Degraded" style="margin:0.75rem 0 0">${esc(snap.error || "Connect metrics to see live rates.")}</p>` : ""}
      ${metricsLine ? `<p class="muted small" style="margin:0.5rem 0 0">${metricsLine}</p>` : ""}
    </div>

    <div class="card">
      <h2>Instances</h2>
      ${liveInstancesTable(snap.instances || [])}
    </div>

    <div class="card">
      <h2>Hot domains <span class="badge">by RPS</span></h2>
      ${!metricsOk
        ? emptyStateHtml("metrics-config", { title: "Metrics not connected", detail: snap.error })
        : liveEntityTable(snap.domains || [], { kind: "domain" })}
    </div>

    <div class="card">
      <h2>Hot endpoints <span class="badge">top by RPS</span></h2>
      ${!metricsOk
        ? emptyStateHtml("metrics-config", { title: "Metrics not connected", detail: snap.error })
        : liveEntityTable(snap.endpoints || [], { kind: "endpoint" })}
    </div>

    ${(snap.quietDomains || []).length ? `
    <div class="card">
      <h2>Quiet domains <span class="badge muted">RPS ≈ 0</span></h2>
      <p class="muted" style="margin:0 0 0.5rem">Configured domains with no traffic in the last ${esc(lookback)}.</p>
      <p style="margin:0">${snap.quietDomains.map((n) =>
        `<a class="badge" href="#/domains?name=${encodeURIComponent(n)}"><code>${esc(n)}</code></a>`).join(" ")}</p>
    </div>` : ""}

    <p class="muted small"><a href="#/metrics">Metrics</a> for history · <a href="#/overview">Overview</a> for the selected time range</p>
  `, soft);

  bindEmptyStateActions(main());
  main().querySelectorAll("[data-goto-hints]").forEach((el) => {
    if (el.dataset.boundHints === "1") return;
    el.dataset.boundHints = "1";
    const go = (ev) => {
      ev.preventDefault();
      location.hash = "#/hints";
    };
    el.addEventListener("click", go);
    el.addEventListener("keydown", (ev) => {
      if (ev.key === "Enter" || ev.key === " ") go(ev);
    });
  });
}

function liveInstancesTable(list) {
  if (!list.length) {
    return emptyStateHtml("config", {
      title: "No instances configured",
      detail: "Add targets under AdminConsole:Instances.",
    });
  }
  return `
    <table class="dense entity-table instances-table">
      <thead>
        <tr>
          <th>Id</th><th>Status</th><th>URL</th>
          <th title="${esc(METRIC_TITLES.liveRps)}">RPS</th>
          <th class="col-uptime">Uptime</th><th>Latency</th><th>Error</th>
        </tr>
      </thead>
      <tbody>
        ${list.map((i) => {
          const st = instanceStatus(i.status);
          return `<tr class="clickable entity-row" data-entity="instance" data-id="${esc(i.id)}" onclick="location.hash='#/instances?id=${encodeURIComponent(i.id)}'">
            <td class="col-name"><code>${esc(i.id)}</code></td>
            <td class="status-${esc(st)}">${esc(st)}</td>
            <td><code class="cell-ellipsis" title="${esc(i.url)}">${esc(i.url)}</code></td>
            <td class="col-num col-rate">${i.requestRate != null ? fmtRequestRate(i.requestRate) : "—"}</td>
            <td class="col-uptime">${esc(formatUptime(i.uptimeSeconds))}</td>
            <td>${formatLatencyMs(i.latencyMs)}</td>
            <td class="muted"><span class="cell-ellipsis" title="${esc(i.error || "")}">${esc(i.error || "—")}</span></td>
          </tr>`;
        }).join("")}
      </tbody>
    </table>`;
}

function liveEntityTable(list, { kind }) {
  if (!list.length) {
    return emptyStateHtml(kind === "endpoint" ? "endpoints" : "domains", {
      title: kind === "endpoint" ? "No live endpoint traffic" : "No live domain traffic",
      detail: "No traffic in the last minute.",
    });
  }
  const isEp = kind === "endpoint";
  return `
    <table class="dense entity-table">
      <thead>
        <tr>
          <th>${isEp ? "Route" : "Domain"}</th>
          ${isEp ? "<th>Domain</th>" : ""}
          <th title="${esc(METRIC_TITLES.liveRps)}">RPS</th>
          <th>OC hit %</th><th>FC hit %</th><th>Factory %</th><th>Fail %</th>
        </tr>
      </thead>
      <tbody>
        ${list.map((e) => {
          const href = isEp
            ? `#/endpoints?route=${encodeURIComponent(e.name)}`
            : `#/domains?name=${encodeURIComponent(e.name)}`;
          return `<tr class="clickable" onclick="location.hash='${href}'">
            <td class="col-name"><code>${esc(e.name)}</code></td>
            ${isEp ? `<td>${e.domain ? `<code>${esc(e.domain)}</code>` : "—"}</td>` : ""}
            <td class="col-num col-rate">${fmtRequestRate(e.requestRate)}</td>
            <td>${shareOrDash(e.ocHitShare)}</td>
            <td>${shareOrDash(e.fcHitShare)}</td>
            <td>${shareOrDash(e.factoryShare)}</td>
            <td>${shareOrDash(e.factoryFailShare)}</td>
          </tr>`;
        }).join("")}
      </tbody>
    </table>`;
}
