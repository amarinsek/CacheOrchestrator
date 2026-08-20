/**
 * Shared helpers used by multiple Admin SPA page modules.
 */

import { api } from "./api.js";
import { navigate } from "./router.js";
import { emptyStateHtml } from "./tables.js";
import {
  appendMetricsRangeParams,
  setMetricsCapability,
} from "./time-range.js";

export function bindGotoHints(root) {
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
 * Prometheus window stats (only source of traffic counters).
 * Returns null when Metrics store is not usable.
 */
export async function fetchWindowStatsIfNeeded(domainsCsv) {
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
export function withWindowInstanceTraffic(instances, windowStats) {
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
export function sliceWindowStatsForInstance(windowStats, instanceId, reportedId) {
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
  return {
    domains: domainsOnInst,
    endpoints: endpointsOnInst,
    pipeline: aggregatePipelineFromStatsRows(domainsOnInst),
  };
}

/**
 * Rebuild pipeline shares from per-row OC/FC counters (same formula as AdminStatsMath.BuildAll).
 * Used for instance detail: cluster window stats are not instance-scoped.
 * @param {Array<{ oc?: object, fc?: object }>|null|undefined} rows
 * @returns {object|null}
 */
export function aggregatePipelineFromStatsRows(rows) {
  let ocHits = 0, ocMisses = 0, ocBypass = 0, ocOff = 0;
  let fcHits = 0, fcMisses = 0, fcStale = 0, fcBypass = 0;
  let factoryRuns = 0;
  for (const r of rows || []) {
    const oc = r.oc || {};
    const fc = r.fc || {};
    ocHits += Number(oc.hits) || 0;
    ocMisses += Number(oc.misses) || 0;
    ocBypass += Number(oc.bypass) || 0;
    ocOff += Number(oc.off) || 0;
    fcHits += Number(fc.hits) || 0;
    fcMisses += Number(fc.misses) || 0;
    fcStale += Number(fc.stale) || 0;
    fcBypass += Number(fc.bypass) || 0;
    factoryRuns += Number(fc.factoryRuns) || 0;
  }
  const ocTraffic = ocHits + ocMisses + ocBypass + ocOff;
  const fcTraffic = fcHits + fcMisses + fcStale + fcBypass;
  const requests = ocTraffic > 0 ? ocTraffic : (fcTraffic > 0 ? fcTraffic : factoryRuns);
  if (requests <= 0) return null;
  const share = (n) => n / requests;
  return {
    ocHitShare: share(ocHits),
    fcHitShare: share(fcHits),
    staleShare: share(fcStale),
    factoryShare: share(factoryRuns),
    otherShare: share(Math.max(0, requests - ocHits - fcHits - factoryRuns)),
  };
}

export function metricsRequiredEmpty(detail) {
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

/** KPI color: green only when every configured instance is healthy. */
export function instancesUpClass(o) {
  const total = (o.instances || []).length
    || ((o.healthyCount || 0) + (o.degradedCount || 0) + (o.downCount || 0));
  const up = o.healthyCount ?? 0;
  const down = o.downCount ?? 0;
  const deg = o.degradedCount ?? 0;
  if (total === 0 || down > 0 || up < total) return "status-Down";
  if (deg > 0) return "status-Degraded";
  return "status-Healthy";
}

/** Truncate long probe errors (e.g. accidental HTML bodies) for the Operations table. */
export function shortError(msg) {
  const s = String(msg || "").replace(/\s+/g, " ").trim();
  if (s.length <= 120) return s;
  return s.slice(0, 117) + "…";
}
