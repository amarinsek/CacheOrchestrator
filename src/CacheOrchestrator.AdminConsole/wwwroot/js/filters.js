/**
 * Toolbar filters: multi-select (All | filter | none), sort option lists,
 * client-side sort/search for domains and instances.
 */

import { esc } from "./format.js";

// —— URL query helpers for multi-select filters ——

/**
 * Parse a CSV query param into selection state.
 * @returns {null|string[]} null = All (no filter), [] = explicit none, [...] = filter
 */
export function parseCsvParam(params, key) {
  const raw = params.get(key) || "";
  if (!raw) return null;
  if (raw === "__none__") return [];
  return raw.split(",").map((s) => s.trim()).filter(Boolean);
}

/**
 * Encode multi-select state back into a query value.
 * null/undefined → omit (All). [] → `__none__`. otherwise join with commas.
 */
export function csvParamFromSelection(ids) {
  if (ids === null || ids === undefined) return "";
  if (ids.length === 0) return "__none__";
  return ids.join(",");
}

/**
 * Multi-select control HTML.
 * @param {string} id Control id used for data-* hooks
 * @param {string} label Visible label
 * @param {{id: string, label: string}[]} options
 * @param {null|string[]} selectedIds null=All, []=none, array=filter
 */
export function multiSelectHtml(id, label, options, selectedIds) {
  const mode = selectedIds === null || selectedIds === undefined
    ? "all"
    : selectedIds.length === 0
      ? "none"
      : "filter";
  let summary = "All";
  if (mode === "none") summary = "None";
  else if (mode === "filter") {
    summary = selectedIds.length <= 2
      ? selectedIds.join(", ")
      : `${selectedIds.length} selected`;
  }
  return `
    <div class="ms" data-ms="${esc(id)}">
      <span class="ms-label">${esc(label)}</span>
      <button type="button" class="ms-btn" data-ms-toggle="${esc(id)}">${esc(summary)} ▾</button>
      <div class="ms-panel hidden" data-ms-panel="${esc(id)}" data-ms-mode="${mode}">
        <div class="ms-actions">
          <button type="button" class="secondary" data-ms-all="${esc(id)}">All</button>
          <button type="button" class="secondary" data-ms-none="${esc(id)}">None</button>
        </div>
        ${options.map((o) => {
          const checked = mode === "all" || (mode === "filter" && selectedIds.includes(o.id));
          return `<label><input type="checkbox" value="${esc(o.id)}" ${checked ? "checked" : ""}/> ${esc(o.label)}</label>`;
        }).join("")}
      </div>
    </div>`;
}

/** Wire click/change handlers for multi-selects inside `root`. */
export function bindMultiSelects(root) {
  root.querySelectorAll("[data-ms-toggle]").forEach((btn) => {
    btn.addEventListener("click", (ev) => {
      ev.preventDefault();
      ev.stopPropagation();
      const id = btn.dataset.msToggle;
      const panel = root.querySelector(`[data-ms-panel="${id}"]`);
      root.querySelectorAll("[data-ms-panel]").forEach((p) => {
        if (p !== panel) p.classList.add("hidden");
      });
      panel?.classList.toggle("hidden");
    });
  });
  root.querySelectorAll("[data-ms-all]").forEach((btn) => {
    btn.addEventListener("click", (ev) => {
      ev.preventDefault();
      const id = btn.dataset.msAll;
      const panel = root.querySelector(`[data-ms-panel="${id}"]`);
      if (panel) panel.dataset.msMode = "all";
      root.querySelectorAll(`[data-ms-panel="${id}"] input[type=checkbox]`).forEach((c) => {
        c.checked = true;
      });
      updateMsSummary(root, id);
    });
  });
  root.querySelectorAll("[data-ms-none]").forEach((btn) => {
    btn.addEventListener("click", (ev) => {
      ev.preventDefault();
      const id = btn.dataset.msNone;
      const panel = root.querySelector(`[data-ms-panel="${id}"]`);
      if (panel) panel.dataset.msMode = "none";
      root.querySelectorAll(`[data-ms-panel="${id}"] input[type=checkbox]`).forEach((c) => {
        c.checked = false;
      });
      updateMsSummary(root, id);
    });
  });
  root.querySelectorAll("[data-ms-panel] input[type=checkbox]").forEach((cb) => {
    cb.addEventListener("change", () => {
      const panel = cb.closest("[data-ms-panel]");
      const id = panel?.dataset.msPanel;
      if (panel) panel.dataset.msMode = "filter";
      if (id) updateMsSummary(root, id);
    });
  });
  if (!window.__msOutsideBound) {
    window.__msOutsideBound = true;
    document.addEventListener("click", closeMsOutside);
  }
}

function closeMsOutside(ev) {
  if (ev.target.closest("[data-ms]")) return;
  document.querySelectorAll("[data-ms-panel]").forEach((p) => p.classList.add("hidden"));
}

function updateMsSummary(root, id) {
  const panel = root.querySelector(`[data-ms-panel="${id}"]`);
  const btn = root.querySelector(`[data-ms-toggle="${id}"]`);
  if (!panel || !btn) return;
  const mode = panel.dataset.msMode || "all";
  const boxes = [...panel.querySelectorAll("input[type=checkbox]")];
  const checked = boxes.filter((c) => c.checked).map((c) => c.value);
  let summary = "All";
  if (mode === "none" || (mode === "filter" && checked.length === 0)) summary = "None";
  else if (mode === "filter") {
    summary = checked.length <= 2 ? checked.join(", ") : `${checked.length} selected`;
  }
  btn.textContent = summary + " ▾";
}

/**
 * @returns {null|string[]} null = All, [] = none, [...] = filter
 */
export function readMultiSelect(root, id) {
  const panel = root.querySelector(`[data-ms-panel="${id}"]`);
  if (!panel) return null;
  const mode = panel.dataset.msMode || "all";
  if (mode === "all") return null;
  const boxes = [...panel.querySelectorAll("input[type=checkbox]")];
  const checked = boxes.filter((c) => c.checked).map((c) => c.value);
  if (mode === "none") return [];
  return checked;
}

// —— Toolbar controls ——

/** Apply button aligned with filter fields (label spacer + compact control). */
export function applyButtonHtml() {
  return `<label class="toolbar-apply"><span>&nbsp;</span><button type="submit">Apply</button></label>`;
}

/** Named sort `<select>` for form toolbars. options: [value, label][] */
export function sortSelectHtml(name, current, options) {
  return `
    <label>Sort
      <select name="${esc(name)}">
        ${options.map(([value, label]) =>
          `<option value="${esc(value)}" ${value === current ? "selected" : ""}>${esc(label)}</option>`
        ).join("")}
      </select>
    </label>`;
}

/** Inline sort control for card headers (Overview). */
export function inlineSortSelectHtml(id, current, options) {
  return `
    <label class="inline-sort">Sort
      <select id="${esc(id)}">
        ${options.map(([value, label]) =>
          `<option value="${esc(value)}" ${value === current ? "selected" : ""}>${esc(label)}</option>`
        ).join("")}
      </select>
    </label>`;
}

/** Sort keys offered on Endpoints list / Overview top-5. */
export const EP_SORT_OPTS = [
  ["requests", "Requests"],
  ["peakRequestRate", "PRPS"],
  ["outputCacheHitShare", "OC hit %"],
  ["dataCacheHitShare", "DC hit %"],
  ["factoryShare", "FA run %"],
  ["factoryFailures", "FAFC"],
  ["avgFactoryDuration", "FAD"],
  ["staleShare", "DC stale %"],
  ["estTimeSaved", "EFTS"],
  ["route", "Route"],
];

/** Sort keys for Domains list. */
export const DOMAIN_SORT_OPTS = [
  ["requests", "Requests"],
  ["peakRequestRate", "PRPS"],
  ["name", "Name"],
  ["outputCacheHitShare", "OC hit %"],
  ["dataCacheHitShare", "DC hit %"],
  ["factoryShare", "FA run %"],
  ["factoryFailures", "FAFC"],
  ["avgFactoryDuration", "FAD"],
  ["staleShare", "DC stale %"],
  ["estTimeSaved", "EFTS"],
  ["invalidations", "Invalidations"],
];

/** Sort keys for Instances list / Overview instances. */
export const INST_SORT_OPTS = [
  ["id", "Id"],
  ["status", "Status"],
  ["requests", "Requests"],
  ["uptime", "Uptime"],
  ["latency", "Latency"],
];

function cmpNumDesc(a, b) {
  return (b ?? -Infinity) - (a ?? -Infinity);
}

/** Shared numeric sort keys for domain/endpoint traffic rows. */
function sortByTrafficMetrics(arr, sort, nameKey) {
  switch (sort) {
    case "factoryShare":
    case "originShare":
      arr.sort((a, b) => cmpNumDesc(
        a.dataCache?.factoryShare ?? a.dataCache?.originShare,
        b.dataCache?.factoryShare ?? b.dataCache?.originShare));
      return true;
    case "staleShare":
      arr.sort((a, b) => cmpNumDesc(a.dataCache?.staleShare, b.dataCache?.staleShare));
      return true;
    case "factoryFailures":
      arr.sort((a, b) => cmpNumDesc(a.dataCache?.factoryFailures, b.dataCache?.factoryFailures));
      return true;
    case "avgFactoryDuration":
      arr.sort((a, b) => cmpNumDesc(a.impact?.avgFactoryDurationMs, b.impact?.avgFactoryDurationMs));
      return true;
    case "factoryAvoidance":
      arr.sort((a, b) => cmpNumDesc(a.impact?.factoryAvoidance, b.impact?.factoryAvoidance));
      return true;
    case "estTimeSaved":
      arr.sort((a, b) => cmpNumDesc(a.impact?.estFactoryTimeSavedMs, b.impact?.estFactoryTimeSavedMs));
      return true;
    case "outputCacheHitShare":
      arr.sort((a, b) => cmpNumDesc(a.outputCache?.hitShare, b.outputCache?.hitShare));
      return true;
    case "dataCacheHitShare":
      arr.sort((a, b) => cmpNumDesc(a.dataCache?.hitShare, b.dataCache?.hitShare));
      return true;
    case "peakRequestRate":
    case "requestRate":
      arr.sort((a, b) => cmpNumDesc(
        a.peakRequestRate ?? a._requestRate,
        b.peakRequestRate ?? b._requestRate));
      return true;
    case "requests":
      arr.sort((a, b) => cmpNumDesc(a.requests, b.requests));
      return true;
    case "name":
    case "route":
      if (nameKey) {
        arr.sort((a, b) => (a[nameKey] || "").localeCompare(b[nameKey] || ""));
        return true;
      }
      return false;
    default:
      return false;
  }
}

/** Client-side endpoint sort (also used after API returns a page). */
export function sortEndpoints(list, sort) {
  const arr = [...(list || [])];
  if (sortByTrafficMetrics(arr, sort, "route")) return arr;
  arr.sort((a, b) => cmpNumDesc(a.requests, b.requests));
  return arr;
}

export function sortDomains(list, sort) {
  const arr = [...(list || [])];
  if (sort === "invalidations") {
    arr.sort((a, b) => cmpNumDesc(a.invalidations, b.invalidations));
    return arr;
  }
  if (sortByTrafficMetrics(arr, sort, "name")) return arr;
  arr.sort((a, b) => cmpNumDesc(a.requests, b.requests));
  return arr;
}

const STATUS_RANK = { Healthy: 0, Degraded: 1, Down: 2, 0: 0, 1: 1, 2: 2 };

export function sortInstances(list, sort) {
  const arr = [...(list || [])];
  switch (sort) {
    case "status":
      arr.sort((a, b) =>
        (STATUS_RANK[a.status] ?? 9) - (STATUS_RANK[b.status] ?? 9)
        || (a.id || "").localeCompare(b.id || ""));
      break;
    case "requests":
      arr.sort((a, b) => cmpNumDesc(a.requests, b.requests));
      break;
    case "uptime":
      arr.sort((a, b) => cmpNumDesc(a.uptimeSeconds, b.uptimeSeconds));
      break;
    case "latency":
      arr.sort((a, b) => (a.latencyMs ?? Infinity) - (b.latencyMs ?? Infinity));
      break;
    case "id":
    default:
      arr.sort((a, b) => (a.id || "").localeCompare(b.id || ""));
      break;
  }
  return arr;
}

export function filterDomainsBySearch(list, search) {
  const q = (search || "").trim().toLowerCase();
  if (!q) return list || [];
  return (list || []).filter((d) => (d.name || "").toLowerCase().includes(q));
}

export function filterInstancesBySearch(list, search) {
  const q = (search || "").trim().toLowerCase();
  if (!q) return list || [];
  return (list || []).filter((i) =>
    (i.id || "").toLowerCase().includes(q)
    || (i.url || "").toLowerCase().includes(q)
    || (i.reportedInstanceId || "").toLowerCase().includes(q));
}
