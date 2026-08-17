/**
 * Shared entity list tables and empty / connectivity chrome.
 *
 * Column contracts (list surfaces — keep stable across pages):
 * - Endpoints: Route | Domain | Req | Pipeline | … | Benefit | Candidate | Hints
 * - Domains:   Domain | Version | Req | Inv | Pipeline | … | Benefit | Candidate | Hints | Ops
 * - Instances: Id | Status | URL | Req | Uptime | Latency | Error | Hints
 * Layer rates (e.g. FC miss rate) stay on detail views only.
 */

import { instanceStatus } from "./api.js";
import {
  esc,
  formatLatencyMs,
  formatUptime,
  factoryShareOf,
  fmtDurationMs,
  impactBandLabel,
  METRIC_TITLES,
  num,
  pct,
  pipelineBar,
  thMetric,
} from "./format.js";
import { hintBadges, severityStack } from "./hints.js";
import { navigate } from "./router.js";

// —— Endpoint table ——

export function endpointRowHtml(e) {
  const domainCell = e.configuredDomain
    ? `<a href="#/domains?name=${encodeURIComponent(e.configuredDomain)}">${esc(e.configuredDomain)}</a>`
    : "—";
  return `<tr class="clickable entity-row" data-entity="endpoint" data-route="${esc(e.route)}">
    <td class="col-name"><code>${esc(e.route)}</code></td>
    <td class="col-domain">${domainCell}</td>
    <td class="col-num">${num(e.requests)}</td>
    <td class="col-pipe">${pipelineBar(e.pipeline)}</td>
    <td class="col-metric">${pct(e.oc?.hitShare, e.oc?.lowRequestSample, "request")}</td>
    <td class="col-metric">${pct(e.fc?.hitShare, e.fc?.lowRequestSample, "request")}</td>
    <td class="col-metric">${pct(factoryShareOf(e.fc), e.fc?.lowRequestSample, "request")}</td>
    <td class="col-metric" title="${esc(METRIC_TITLES.estTimeSaved)}">${fmtDurationMs(e.impact?.estFactoryTimeSavedMs)}</td>
    <td class="col-metric" title="${esc(METRIC_TITLES.cacheBenefit)}">${impactBandLabel(e.impact?.benefit, { html: true })}</td>
    <td class="col-metric" title="${esc(METRIC_TITLES.cacheCandidate)}">${impactBandLabel(e.impact?.candidate, { html: true })}</td>
    <td class="col-hints">${hintBadges(e.hints)}</td>
  </tr>`;
}

export function endpointTableHtml(list, emptyCtx = {}) {
  if (!list || !list.length) {
    return emptyStateHtml(emptyCtx.kind || "endpoints", emptyCtx);
  }
  return `
    <table class="dense entity-table endpoints-table">
      <thead>
        <tr>
          ${thMetric("Route", "route", { fromKey: true })}
          ${thMetric("Domain", "domain", { fromKey: true })}
          ${thMetric("Req", "req", { fromKey: true })}
          ${thMetric("Pipeline", "pipeline", { fromKey: true })}
          ${thMetric("OC hit %", "ocHitShare", { fromKey: true })}
          ${thMetric("FC hit %", "fcHitShare", { fromKey: true })}
          ${thMetric("Factory %", "factoryShare", { fromKey: true })}
          ${thMetric("Time saved", "estTimeSaved", { fromKey: true })}
          ${thMetric("Benefit", "cacheBenefit", { fromKey: true })}
          ${thMetric("Candidate", "cacheCandidate", { fromKey: true })}
          ${thMetric("Hints", "hints", { fromKey: true })}
        </tr>
      </thead>
      <tbody>${list.map(endpointRowHtml).join("")}</tbody>
    </table>`;
}

// —— Domain table ——

export function domainRowHtml(d) {
  return `<tr class="clickable entity-row" data-entity="domain" data-name="${esc(d.name)}">
    <td class="col-name"><code>${esc(d.name)}</code>${d.versionIsRuntimeOverride ? ' <span class="badge">rt</span>' : ""}</td>
    <td class="col-metric"><span class="cell-ellipsis" title="${esc(d.version)}">${esc(d.version)}</span></td>
    <td class="col-num">${num(d.requests)}</td>
    <td class="col-num" title="${esc(METRIC_TITLES.inv)}">${num(d.invalidations)}</td>
    <td class="col-pipe">${pipelineBar(d.pipeline)}</td>
    <td class="col-metric">${pct(d.oc?.hitShare, d.oc?.lowRequestSample, "request")}</td>
    <td class="col-metric">${pct(d.fc?.hitShare, d.fc?.lowRequestSample, "request")}</td>
    <td class="col-metric">${pct(factoryShareOf(d.fc), d.fc?.lowRequestSample, "request")}</td>
    <td class="col-metric" title="${esc(METRIC_TITLES.estTimeSaved)}">${fmtDurationMs(d.impact?.estFactoryTimeSavedMs)}</td>
    <td class="col-metric" title="${esc(METRIC_TITLES.cacheBenefit)}">${impactBandLabel(d.impact?.benefit, { html: true })}</td>
    <td class="col-metric" title="${esc(METRIC_TITLES.cacheCandidate)}">${impactBandLabel(d.impact?.candidate, { html: true })}</td>
    <td class="col-hints">${hintBadges(d.hints)}</td>
    <td class="col-ops"><a href="#/operations?domain=${encodeURIComponent(d.name)}" onclick="event.stopPropagation()">Ops</a></td>
  </tr>`;
}

export function domainTableHtml(list, emptyCtx = {}) {
  if (!list || !list.length) {
    return emptyStateHtml(emptyCtx.kind || "domains", emptyCtx);
  }
  return `
    <table class="dense entity-table domains-table">
      <thead>
        <tr>
          ${thMetric("Domain", "domain", { fromKey: true })}
          ${thMetric("Version", "version", { fromKey: true })}
          ${thMetric("Req", "req", { fromKey: true })}
          ${thMetric("Inv", "inv", { fromKey: true })}
          ${thMetric("Pipeline", "pipeline", { fromKey: true })}
          ${thMetric("OC hit %", "ocHitShare", { fromKey: true })}
          ${thMetric("FC hit %", "fcHitShare", { fromKey: true })}
          ${thMetric("Factory %", "factoryShare", { fromKey: true })}
          ${thMetric("Time saved", "estTimeSaved", { fromKey: true })}
          ${thMetric("Benefit", "cacheBenefit", { fromKey: true })}
          ${thMetric("Candidate", "cacheCandidate", { fromKey: true })}
          ${thMetric("Hints", "hints", { fromKey: true })}
          <th></th>
        </tr>
      </thead>
      <tbody>${list.map(domainRowHtml).join("")}</tbody>
    </table>`;
}

// —— Instance table ——

export function instanceRowHtml(i) {
  const started = i.startedAtUtc
    ? new Date(i.startedAtUtc).toISOString().replace("T", " ").replace(/\.\d+Z$/, "Z")
    : "";
  const up = formatUptime(i.uptimeSeconds);
  const st = instanceStatus(i.status);
  return `<tr class="clickable entity-row" data-entity="instance" data-id="${esc(i.id)}">
    <td class="col-name"><code>${esc(i.id)}</code></td>
    <td class="status-${esc(st)}">${esc(st)}</td>
    <td><code class="cell-ellipsis" title="${esc(i.url)}">${esc(i.url)}</code></td>
    <td class="col-num">${num(i.requests)}</td>
    <td title="${esc(started || "start time unknown")}">${esc(up)}</td>
    <td>${formatLatencyMs(i.latencyMs)}</td>
    <td class="muted"><span class="cell-ellipsis" title="${esc(i.error || "")}">${esc(i.error || "—")}</span></td>
    <td class="col-hints">${severityStack(i.hintSummary)}</td>
  </tr>`;
}

export function instanceTableHtml(list, emptyCtx = {}) {
  if (!list || !list.length) {
    return emptyStateHtml(emptyCtx.kind || "config", {
      title: "No instances configured",
      detail: "Add targets under AdminConsole:Instances in Admin Console App appsettings, then refresh.",
      ...emptyCtx,
    });
  }
  return `
    <table class="dense entity-table instances-table">
      <thead>
        <tr>
          ${thMetric("Id", "instance", { fromKey: true })}
          ${thMetric("Status", "status", { fromKey: true })}
          ${thMetric("URL", "url", { fromKey: true })}
          ${thMetric("Req", "req", { fromKey: true })}
          ${thMetric("Uptime", "uptime", { fromKey: true })}
          ${thMetric("Latency", "latency", { fromKey: true })}
          ${thMetric("Error", "error", { fromKey: true })}
          ${thMetric("Hints", "hints", { fromKey: true })}
        </tr>
      </thead>
      <tbody>${list.map(instanceRowHtml).join("")}</tbody>
    </table>`;
}

// —— Empty / offline ——

/** Shared empty / offline panels for list surfaces. */
export function emptyStateHtml(kind, ctx = {}) {
  const map = {
    config: {
      cls: "config",
      icon: "◎",
      title: ctx.title || "Nothing configured",
      detail: ctx.detail || "Configure AdminConsole:Instances and enable Local Admin on target apps.",
    },
    offline: {
      cls: "offline",
      icon: "⏻",
      title: ctx.title || "Target apps unreachable",
      detail: ctx.detail
        || "All configured instances are down or timed out. Entity lists need at least one healthy Local Admin API.",
    },
    endpoints: {
      cls: "filter",
      icon: "◫",
      title: ctx.title || "No endpoints",
      detail: ctx.detail
        || "No endpoint counters match the current filters, or apps have not served traffic yet.",
    },
    domains: {
      cls: "filter",
      icon: "◫",
      title: ctx.title || "No domains",
      detail: ctx.detail
        || "No domains to show for the current instance filter / connectivity state.",
    },
    filter: {
      cls: "filter",
      icon: "⊘",
      title: ctx.title || "No matches",
      detail: ctx.detail || "Adjust filters or choose All.",
    },
    error: {
      cls: "offline",
      icon: "!",
      title: ctx.title || "Failed to load",
      detail: ctx.detail || "Request failed. Check Admin Console App logs and instance URLs.",
    },
    "metrics-config": {
      cls: "config",
      icon: "◎",
      title: ctx.title || "Metrics storage not configured",
      detail: ctx.detail
        || "Set AdminConsole:Metrics:Enabled, Provider (Prometheus), and BaseUrl.",
    },
    "metrics-offline": {
      cls: "offline",
      icon: "⏻",
      title: ctx.title || "Metrics storage not connected",
      detail: ctx.detail || "Prometheus probe failed. Check URL, network, and auth.",
    },
    "metrics-empty": {
      cls: "filter",
      icon: "◫",
      title: ctx.title || "No metric samples",
      detail: ctx.detail
        || "Connected, but no series in this range. Confirm the CacheOrchestrator meter is scraped.",
    },
  };
  const m = map[kind] || map.filter;
  const actions = ctx.actions || [
    { label: "Refresh", onclick: "window.__adminRefresh && window.__adminRefresh()" },
    { label: "Instances", href: "#/instances" },
  ];
  return `
    <div class="empty-state ${esc(m.cls)}">
      <div class="es-icon">${m.icon}</div>
      <h3>${esc(m.title)}</h3>
      <p>${esc(m.detail)}</p>
      <div class="es-actions">
        ${actions.map((a) => a.href
          ? `<a class="btn-secondary" href="${esc(a.href)}">${esc(a.label)}</a>`
          : `<button type="button" class="secondary" data-es-action="${esc(a.label)}">${esc(a.label)}</button>`
        ).join("")}
      </div>
    </div>`;
}

/** Banner above lists when instances are missing / partial / all-down. */
export function connectivityBanner(instances) {
  const list = instances || [];
  if (!list.length) {
    return `<div class="banner warn">
      <span>No instances in <code>AdminConsole:Instances</code>.</span>
      <span class="banner-actions"><button type="button" class="secondary" data-es-refresh>Refresh</button></span>
    </div>`;
  }
  const up = list.filter((i) => instanceStatus(i.status) === "Healthy").length;
  const down = list.filter((i) => instanceStatus(i.status) === "Down").length;
  const deg = list.filter((i) => instanceStatus(i.status) === "Degraded").length;
  if (down === list.length) {
    return `<div class="banner err">
      <span><strong>All instances down</strong> — entity data cannot be loaded from Local Admin APIs.
        ${list.map((i) => `<code>${esc(i.id)}</code>`).join(", ")}</span>
      <span class="banner-actions"><button type="button" class="secondary" data-es-refresh>Retry</button></span>
    </div>`;
  }
  if (down > 0 || deg > 0) {
    return `<div class="banner warn">
      <span>Partial connectivity: <strong>${up}</strong> healthy
        ${down ? `· <strong>${down}</strong> down` : ""}
        ${deg ? `· <strong>${deg}</strong> degraded` : ""}
      </span>
      <span class="banner-actions"><a href="#/instances">View instances</a></span>
    </div>`;
  }
  return "";
}

export function bindEmptyStateActions(root) {
  root.querySelectorAll("[data-es-refresh], [data-es-action=Refresh], [data-es-action=Retry]").forEach((btn) => {
    btn.addEventListener("click", () => window.__adminRefresh?.());
  });
}

export function allInstancesDown(instances) {
  const list = instances || [];
  return list.length > 0 && list.every((i) => instanceStatus(i.status) === "Down");
}

export function noInstancesConfigured(instances) {
  return !instances || instances.length === 0;
}

/** Row click → detail hash routes. */
export function bindEntityTableClicks(root) {
  root.querySelectorAll("tr.entity-row[data-entity=endpoint]").forEach((tr) => {
    tr.addEventListener("click", (ev) => {
      if (ev.target.closest("a")) return;
      navigate("endpoints", { route: tr.dataset.route });
    });
  });
  root.querySelectorAll("tr.entity-row[data-entity=domain]").forEach((tr) => {
    tr.addEventListener("click", (ev) => {
      if (ev.target.closest("a")) return;
      navigate("domains", { name: tr.dataset.name });
    });
  });
  root.querySelectorAll("tr.entity-row[data-entity=instance]").forEach((tr) => {
    tr.addEventListener("click", () => navigate("instances", { id: tr.dataset.id }));
  });
}

// —— Detail layer blocks ——

export function layerDetailOc(oc) {
  if (!oc) return "";
  return `
    <div class="detail-block">
      <h3>Output Cache</h3>
      <div class="kv">
        <span title="Output Cache hits">Hits</span><span>${num(oc.hits)}</span>
        <span title="Output Cache misses">Misses</span><span>${num(oc.misses)}</span>
        <span title="Output Cache bypass (not eligible / skipped)">Bypass</span><span>${num(oc.bypass)}</span>
        <span title="Samples that reached the Output Cache layer">Layer n</span><span>${num(oc.layerSampleSize)}</span>
        <span title="${esc(METRIC_TITLES.ocHitShare)}">OC hit share</span><span>${pct(oc.hitShare, oc.lowRequestSample, "request")}</span>
        <span title="Output Cache miss share of all requests">OC miss share</span><span>${pct(oc.missShare, oc.lowRequestSample, "request")}</span>
        <span title="Output Cache bypass share of all requests">OC bypass share</span><span>${pct(oc.bypassShare, oc.lowRequestSample, "request")}</span>
        <span title="Hit rate of traffic that reached Output Cache">OC hit rate (layer)</span><span>${pct(oc.hitRate, oc.lowSample, "layer")}</span>
        <span title="Miss rate of traffic that reached Output Cache">OC miss rate (layer)</span><span>${pct(oc.missRate, oc.lowSample, "layer")}</span>
      </div>
    </div>`;
}

export function layerDetailFc(fc) {
  if (!fc) return "";
  return `
    <div class="detail-block">
      <h3>FusionCache</h3>
      <div class="kv">
        <span title="FusionCache hits">Hits</span><span>${num(fc.hits)}</span>
        <span title="FusionCache misses">Misses</span><span>${num(fc.misses)}</span>
        <span title="${esc(METRIC_TITLES.stale)}">Stale</span><span>${num(fc.stale)}</span>
        <span title="FusionCache bypass">Bypass</span><span>${num(fc.bypass)}</span>
        <span title="${esc(METRIC_TITLES.factory)}">Factory runs</span><span>${num(fc.factoryRuns)}</span>
        <span title="${esc(METRIC_TITLES.factoryFailures)}">Factory failures</span><span>${num(fc.factoryFailures)}</span>
        <span title="Samples that reached the Fusion layer">Layer n</span><span>${num(fc.layerSampleSize)}</span>
        <span title="${esc(METRIC_TITLES.fcHitShare)}">FC hit share</span><span>${pct(fc.hitShare, fc.lowRequestSample, "request")}</span>
        <span title="${esc(METRIC_TITLES.fcMissShare)}">FC miss share</span><span>${pct(fc.missShare, fc.lowRequestSample, "request")}</span>
        <span title="${esc(METRIC_TITLES.staleShare)}">Stale share</span><span>${pct(fc.staleShare, fc.lowRequestSample, "request")}</span>
        <span title="${esc(METRIC_TITLES.factoryShare)}">Factory share</span><span>${pct(factoryShareOf(fc), fc.lowRequestSample, "request")}</span>
        <span title="${esc(METRIC_TITLES.fcHitRate)}">FC hit rate (layer)</span><span>${pct(fc.hitRate, fc.lowSample, "layer")}</span>
        <span title="${esc(METRIC_TITLES.fcMissRate)}">FC miss rate (layer)</span><span>${pct(fc.missRate, fc.lowSample, "layer")}</span>
        <span title="${esc(METRIC_TITLES.staleRate)}">Stale rate (layer)</span><span>${pct(fc.staleRate, fc.lowSample, "layer")}</span>
      </div>
    </div>`;
}

const TIP_TRACK_LATENCY =
  "N/A — no factory duration samples. Enable Cache:Admin:TrackLatency on the app instance (and generate factory runs).";
const TIP_TRACK_SIZE =
  "N/A — no result-size samples. Enable Cache:Admin:TrackResultSize (cheap types only: string, bytes, seekable stream).";

/** N/A with tooltip when a tracking channel has no samples. */
function naTracking(tip) {
  return `<span class="na-tracking" title="${esc(tip)}">N/A</span>`;
}

/** Cache impact KPIs (Console-derived from raw v2 + factory duration/size). */
export function impactDetailHtml(impact, windowLabel) {
  if (!impact) {
    return `
    <div class="detail-block">
      <h3>Cache impact</h3>
      <p class="muted">No impact KPIs (enable Local Admin stats and prefer instance library ≥ 2.2 with <code>/stats/v2</code>).</p>
    </div>`;
  }
  const win = windowLabel ? ` · ${esc(windowLabel)}` : "";
  const hasDuration = (impact.factoryDurationCount || 0) > 0;
  const hasSize = (impact.factoryResultSizeCount || 0) > 0;
  const avgDur = hasDuration && impact.avgFactoryDurationMs != null
    ? `${esc(String(Math.round(impact.avgFactoryDurationMs * 10) / 10))} ms`
    : naTracking(TIP_TRACK_LATENCY);
  const timeSaved = hasDuration && impact.estFactoryTimeSavedMs != null
    ? fmtDurationMs(impact.estFactoryTimeSavedMs)
    : naTracking(TIP_TRACK_LATENCY);
  const tsr = hasDuration && impact.timeSavedRatio != null
    ? pct(impact.timeSavedRatio)
    : naTracking(TIP_TRACK_LATENCY);
  const avgSize = hasSize && impact.avgFactoryResultSizeBytes != null
    ? fmtBytes(impact.avgFactoryResultSizeBytes)
    : naTracking(TIP_TRACK_SIZE);
  const offload = hasSize && impact.estPayloadOffloadBytes != null
    ? fmtBytes(impact.estPayloadOffloadBytes)
    : naTracking(TIP_TRACK_SIZE);

  return `
    <div class="detail-block">
      <h3>Cache impact${win ? `<span class="badge">${win.replace(/^ · /, "")}</span>` : ""}</h3>
      <div class="kv">
        <span title="${esc(METRIC_TITLES.factoryAvoidance)}">Factory avoidance</span><span>${pct(impact.factoryAvoidance, impact.lowRequestSample, "request")}</span>
        <span title="${esc(METRIC_TITLES.factoryShare)}">Factory % (same traffic mix)</span><span>${pct(impact.factoryShare, impact.lowRequestSample, "request")}</span>
        <span title="Average factory-path duration">Avg factory duration</span><span>${avgDur}</span>
        <span title="${esc(METRIC_TITLES.estTimeSaved)}">Est. factory time saved</span><span>${timeSaved}</span>
        <span title="timeSaved / (timeSaved + factory duration paid)">Time-saved ratio</span><span>${tsr}</span>
        <span title="Average measured factory result size">Avg result size</span><span>${avgSize}</span>
        <span title="Avoided factory calls × avg result size">Est. payload offload</span><span>${offload}</span>
        <span title="${esc(METRIC_TITLES.cacheBenefit)}">Benefit</span><span>${impactBandLabel(impact.benefit, { html: true })}</span>
        <span title="${esc(METRIC_TITLES.cacheCandidate)}">Candidate</span><span>${impactBandLabel(impact.candidate, { html: true })}</span>
        <span title="Factory duration samples (0 if TrackLatency is off)">Duration samples</span><span>${num(impact.factoryDurationCount)}</span>
        <span title="Factory result size samples (0 if TrackResultSize is off)">Size samples</span><span>${num(impact.factoryResultSizeCount)}</span>
      </div>
    </div>`;
}

function fmtBytes(n) {
  if (n == null || Number.isNaN(n)) return "—";
  const v = Number(n);
  if (v < 1024) return `${Math.round(v)} B`;
  if (v < 1024 * 1024) return `${(v / 1024).toFixed(1)} KB`;
  return `${(v / (1024 * 1024)).toFixed(2)} MB`;
}

/** Detail KPI strip: time saved + bands (N/A when duration tracking has no samples). */
export function impactKpiRowHtml(impact) {
  if (!impact) return "";
  const hasDuration = (impact.factoryDurationCount || 0) > 0;
  const timeSaved = hasDuration && impact.estFactoryTimeSavedMs != null
    ? fmtDurationMs(impact.estFactoryTimeSavedMs)
    : naTracking(TIP_TRACK_LATENCY);
  return `
        <div class="kpi"${tipAttrSafe("estTimeSaved")}><div class="label">Time saved</div><div class="value" style="font-size:1rem">${timeSaved}</div></div>
        <div class="kpi"${tipAttrSafe("cacheBenefit")}><div class="label">Benefit</div><div class="value" style="font-size:1rem">${impactBandLabel(impact.benefit, { html: true })}</div></div>
        <div class="kpi"${tipAttrSafe("cacheCandidate")}><div class="label">Candidate</div><div class="value" style="font-size:1rem">${impactBandLabel(impact.candidate, { html: true })}</div></div>`;
}

function tipAttrSafe(key) {
  const t = METRIC_TITLES[key];
  return t ? ` title="${esc(t)}"` : "";
}

