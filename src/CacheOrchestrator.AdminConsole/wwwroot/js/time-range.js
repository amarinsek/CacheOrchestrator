/**
 * Global stats time range for Admin Console (Grafana-style relative + absolute).
 *
 * Modes:
 * - process_totals — cumulative Admin raw counters (no Metrics store required)
 * - windowed relative — Last N (15m…7d) from Prometheus
 * - windowed absolute — custom from/to UTC (Grafana absolute time range)
 */

const STORAGE_KEY = "adminStatsTimeRange";

/**
 * @typedef {{
 *   mode: "process_totals"
 * } | {
 *   mode: "windowed",
 *   range: string,
 *   fromUtc?: string|null,
 *   toUtc?: string|null
 * }} TimeRangeState
 */

export const WINDOW_SHORTCUTS = [
  { id: "15m", label: "Last 15 minutes" },
  { id: "1h", label: "Last 1 hour" },
  { id: "6h", label: "Last 6 hours" },
  { id: "24h", label: "Last 24 hours" },
  { id: "7d", label: "Last 7 days" },
];

export const PROCESS_TOTALS_LABEL = "Process totals";
export const PROCESS_TOTALS_ID = "process_totals";
export const CUSTOM_RANGE_ID = "custom";

/** @type {TimeRangeState} */
let state = load();

/** @type {Set<() => void>} */
const listeners = new Set();

/** @type {"unknown"|"connected"|"disconnected"|"not_configured"} */
let metricsCapability = "unknown";

function load() {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return { mode: "process_totals" };
    const o = JSON.parse(raw);
    if (o?.mode === "windowed") {
      if (o.range === CUSTOM_RANGE_ID && o.fromUtc && o.toUtc) {
        return { mode: "windowed", range: CUSTOM_RANGE_ID, fromUtc: o.fromUtc, toUtc: o.toUtc };
      }
      if (typeof o.range === "string") {
        return { mode: "windowed", range: normalizeRange(o.range), fromUtc: null, toUtc: null };
      }
    }
  } catch { /* ignore */ }
  return { mode: "process_totals" };
}

function persist() {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(state));
  } catch { /* ignore */ }
}

export function normalizeRange(range) {
  const id = String(range || "1h").trim().toLowerCase();
  if (id === CUSTOM_RANGE_ID) return CUSTOM_RANGE_ID;
  return WINDOW_SHORTCUTS.some((s) => s.id === id) ? id : "1h";
}

export function rangeDurationSeconds(range) {
  switch (normalizeRange(range)) {
    case "15m": return 15 * 60;
    case "1h": return 3600;
    case "6h": return 6 * 3600;
    case "24h": return 24 * 3600;
    case "7d": return 7 * 86400;
    default: return 3600;
  }
}

/**
 * Chart X-axis window in unix seconds for the effective selection.
 * @param {string} [range] optional override
 * @param {number} [toSec]
 */
export function chartWindow(range, toSec = Math.floor(Date.now() / 1000)) {
  const abs = getAbsoluteWindow();
  if (abs) {
    return { tMin: abs.tMin, tMax: abs.tMax, range: CUSTOM_RANGE_ID };
  }
  const r = normalizeRange(range || getPromRange() || "1h");
  if (r === CUSTOM_RANGE_ID) {
    const to = Number(toSec) || Math.floor(Date.now() / 1000);
    return { tMin: to - 3600, tMax: to, range: "1h" };
  }
  const to = Number(toSec) || Math.floor(Date.now() / 1000);
  return { tMin: to - rangeDurationSeconds(r), tMax: to, range: r };
}

/** Absolute window when custom from/to is set (unix seconds). */
export function getAbsoluteWindow() {
  if (state.mode !== "windowed" || state.range !== CUSTOM_RANGE_ID) return null;
  if (!state.fromUtc || !state.toUtc) return null;
  const tMin = Math.floor(new Date(state.fromUtc).getTime() / 1000);
  const tMax = Math.floor(new Date(state.toUtc).getTime() / 1000);
  if (!Number.isFinite(tMin) || !Number.isFinite(tMax) || tMax <= tMin) return null;
  return { tMin, tMax, fromUtc: state.fromUtc, toUtc: state.toUtc };
}

export function getTimeRange() {
  return { ...state };
}

export function isProcessTotals() {
  if (state.mode !== "windowed") return true;
  if (metricsCapability === "connected") return false;
  return true;
}

export function isWindowedEffective() {
  return state.mode === "windowed" && metricsCapability === "connected";
}

export function isCustomAbsolute() {
  return isWindowedEffective() && state.range === CUSTOM_RANGE_ID && !!getAbsoluteWindow();
}

/** Relative Prom token when windowed relative; null for process totals or custom. */
export function getPromRange() {
  if (!isWindowedEffective()) return null;
  if (state.range === CUSTOM_RANGE_ID) return null;
  return normalizeRange(state.range || "1h");
}

/**
 * Query args for /api/metrics/series and /summary.
 * @returns {{ range: string, from?: string, to?: string }}
 */
export function getMetricsQueryArgs() {
  if (!isWindowedEffective()) {
    return { range: "1h" };
  }
  const abs = getAbsoluteWindow();
  if (abs) {
    return { range: CUSTOM_RANGE_ID, from: abs.fromUtc, to: abs.toUtc };
  }
  return { range: normalizeRange(state.range || "1h") };
}

/** Append metrics range query params onto URLSearchParams. */
export function appendMetricsRangeParams(q) {
  const a = getMetricsQueryArgs();
  if (a.range) q.set("range", a.range);
  if (a.from) q.set("from", a.from);
  if (a.to) q.set("to", a.to);
  return q;
}

export function getDisplayLabel() {
  if (isProcessTotals()) return PROCESS_TOTALS_LABEL;
  if (state.range === CUSTOM_RANGE_ID && state.fromUtc && state.toUtc) {
    return formatAbsoluteLabel(state.fromUtc, state.toUtc);
  }
  const id = normalizeRange(state.range || "1h");
  return WINDOW_SHORTCUTS.find((s) => s.id === id)?.label || `Last ${id}`;
}

export function getBadgeText() {
  if (isProcessTotals()) return PROCESS_TOTALS_LABEL;
  if (state.range === CUSTOM_RANGE_ID && state.fromUtc && state.toUtc) {
    return "Custom";
  }
  return normalizeRange(state.range || "1h");
}

function formatAbsoluteLabel(fromIso, toIso) {
  try {
    const f = new Date(fromIso);
    const t = new Date(toIso);
    const opts = { month: "short", day: "numeric", hour: "2-digit", minute: "2-digit" };
    return `${f.toLocaleString(undefined, opts)} → ${t.toLocaleString(undefined, opts)}`;
  } catch {
    return "Custom range";
  }
}

export function setMetricsCapability(cap) {
  metricsCapability = cap || "unknown";
  notify();
}

export function getMetricsCapability() {
  return metricsCapability;
}

export function setTimeRange(next) {
  if (!next || typeof next !== "object") return;
  if (next.mode === "process_totals" || next.mode === PROCESS_TOTALS_ID) {
    state = { mode: "process_totals" };
  } else if (next.mode === "windowed" || next.range === CUSTOM_RANGE_ID || next.fromUtc) {
    if (next.range === CUSTOM_RANGE_ID || (next.fromUtc && next.toUtc)) {
      state = {
        mode: "windowed",
        range: CUSTOM_RANGE_ID,
        fromUtc: next.fromUtc || null,
        toUtc: next.toUtc || null,
      };
    } else {
      state = {
        mode: "windowed",
        range: normalizeRange(next.range || "1h"),
        fromUtc: null,
        toUtc: null,
      };
    }
  } else if (typeof next.range === "string") {
    state = { mode: "windowed", range: normalizeRange(next.range), fromUtc: null, toUtc: null };
  }
  persist();
  notify();
}

export function getSelectValue() {
  if (state.mode === "process_totals") return PROCESS_TOTALS_ID;
  if (state.range === CUSTOM_RANGE_ID) return CUSTOM_RANGE_ID;
  return normalizeRange(state.range || "1h");
}

export function setFromSelectValue(value) {
  const v = String(value || "").trim();
  if (v === PROCESS_TOTALS_ID || v === "lifetime" || v === "process") {
    setTimeRange({ mode: "process_totals" });
    return;
  }
  if (v === CUSTOM_RANGE_ID) return; // absolute applied separately
  setTimeRange({ mode: "windowed", range: normalizeRange(v) });
}

export function setAbsoluteRange(fromUtc, toUtc) {
  const f = new Date(fromUtc);
  const t = new Date(toUtc);
  if (Number.isNaN(f.getTime()) || Number.isNaN(t.getTime()) || t <= f) return false;
  setTimeRange({
    mode: "windowed",
    range: CUSTOM_RANGE_ID,
    fromUtc: f.toISOString(),
    toUtc: t.toISOString(),
  });
  return true;
}

export function subscribe(fn) {
  if (typeof fn === "function") listeners.add(fn);
  return () => listeners.delete(fn);
}

function notify() {
  for (const fn of listeners) {
    try { fn(getTimeRange()); } catch { /* ignore */ }
  }
}

export function timeRangeOptionsHtml() {
  // Legacy option list (tests / fallback).
  const cur = getSelectValue();
  const windowDisabled = metricsCapability !== "connected" && metricsCapability !== "unknown";
  const opts = [
    `<option value="${PROCESS_TOTALS_ID}"${cur === PROCESS_TOTALS_ID ? " selected" : ""}>${PROCESS_TOTALS_LABEL}</option>`,
  ];
  for (const s of WINDOW_SHORTCUTS) {
    const dis = windowDisabled ? " disabled" : "";
    opts.push(
      `<option value="${s.id}"${cur === s.id ? " selected" : ""}${dis}>${s.label}</option>`,
    );
  }
  return opts.join("");
}

/** @deprecated Use mountTimeRangePicker */
export function timeRangeSelectHtml(opts = {}) {
  const id = opts.id || "selTimeRange";
  return `<div id="${id}Host"></div>`;
}

export function timeRangeScopeNote() {
  if (isWindowedEffective()) {
    return `Traffic KPIs: <strong>${getDisplayLabel()}</strong> (Metrics store). Config and identity fields are current values.`;
  }
  if (state.mode === "windowed" && metricsCapability !== "connected") {
    return `Showing <strong>${PROCESS_TOTALS_LABEL}</strong> — Metrics store not connected (windowed range kept as preference).`;
  }
  return `Traffic KPIs: <strong>${PROCESS_TOTALS_LABEL}</strong> (cumulative Admin counters since process start).`;
}

// —— Grafana-style picker (button in nav; panel portaled to document.body) ——

/** @type {HTMLElement|null} */
let activePanel = null;
/** @type {HTMLElement|null} */
let activeBtn = null;
/** @type {(() => void)|null} */
let activeOnChange = null;

/**
 * Mount / re-render the range picker into a host element.
 * @param {HTMLElement|null} host
 * @param {{ onChange?: () => void }} [opts]
 */
export function mountTimeRangePicker(host, opts = {}) {
  if (!host) return;
  closeTimeRangePanel();
  host.innerHTML = pickerButtonHtml();
  bindPicker(host, opts.onChange);
}

function pickerButtonHtml() {
  const cap = metricsCapability;
  const windowOk = cap === "connected" || cap === "unknown";
  const label = getDisplayLabel();
  const title = windowOk
    ? "Time range: quick ranges or absolute From/To (like Grafana)"
    : "Metrics store not connected — only Process totals available";
  return `
    <div class="tr-picker" title="${escHtml(title)}">
      <button type="button" class="tr-btn" id="trPickerBtn" aria-haspopup="dialog" aria-expanded="false">
        <span class="tr-btn-label">${escHtml(label)}</span>
        <span class="tr-btn-caret" aria-hidden="true">▾</span>
      </button>
    </div>`;
}

function pickerPanelHtml() {
  const cap = metricsCapability;
  const windowOk = cap === "connected" || cap === "unknown";
  const cur = getSelectValue();

  const relBtns = [
    { id: PROCESS_TOTALS_ID, label: PROCESS_TOTALS_LABEL, always: true },
    ...WINDOW_SHORTCUTS.map((s) => ({ id: s.id, label: s.label, always: false })),
  ].map((s) => {
    const dis = !s.always && !windowOk ? " disabled" : "";
    const active = cur === s.id ? " active" : "";
    return `<button type="button" class="tr-rel-btn${active}" data-tr-rel="${s.id}"${dis}>${escHtml(s.label)}</button>`;
  }).join("");

  const abs = getAbsoluteWindow();
  const fromLocal = abs
    ? toDatetimeLocalValue(new Date(abs.fromUtc))
    : toDatetimeLocalValue(new Date(Date.now() - 3600_000));
  const toLocal = abs
    ? toDatetimeLocalValue(new Date(abs.toUtc))
    : toDatetimeLocalValue(new Date());

  return `
    <div class="tr-panel" id="trPanel" role="dialog" aria-label="Time range">
      <div class="tr-col tr-rel">
        <div class="tr-col-title">Quick ranges</div>
        ${relBtns}
      </div>
      <div class="tr-col tr-abs">
        <div class="tr-col-title">Absolute time range</div>
        <label class="tr-field">From
          <input type="datetime-local" id="trFrom" value="${escHtml(fromLocal)}" ${windowOk ? "" : "disabled"} step="60" />
        </label>
        <label class="tr-field">To
          <input type="datetime-local" id="trTo" value="${escHtml(toLocal)}" ${windowOk ? "" : "disabled"} step="60" />
        </label>
        <button type="button" class="tr-apply" id="trApplyAbs" ${windowOk ? "" : "disabled"}>Apply time range</button>
        <p class="tr-hint">Pick From/To (local date &amp; time), then Apply. Queries use UTC. Or choose a quick range on the left.</p>
      </div>
    </div>`;
}

function bindPicker(host, onChange) {
  const btn = host.querySelector("#trPickerBtn");
  if (!btn) return;
  activeOnChange = onChange || null;

  btn.addEventListener("click", (ev) => {
    ev.preventDefault();
    ev.stopPropagation();
    if (activePanel && !activePanel.classList.contains("hidden") && activeBtn === btn) {
      closeTimeRangePanel();
      return;
    }
    openTimeRangePanel(btn, onChange);
  });
}

function openTimeRangePanel(btn, onChange) {
  closeTimeRangePanel();
  activeOnChange = onChange || null;
  activeBtn = btn;

  const wrap = document.createElement("div");
  wrap.innerHTML = pickerPanelHtml().trim();
  const panel = wrap.firstElementChild;
  if (!panel) return;
  document.body.appendChild(panel);
  activePanel = panel;
  btn.setAttribute("aria-expanded", "true");

  positionPanel(btn, panel);

  panel.querySelectorAll("[data-tr-rel]").forEach((b) => {
    b.addEventListener("click", (ev) => {
      ev.preventDefault();
      ev.stopPropagation();
      if (b.disabled) return;
      setFromSelectValue(b.getAttribute("data-tr-rel"));
      closeTimeRangePanel();
      paintPickerLabel(btn.closest(".tr-picker")?.parentElement || btn.parentElement);
      onChange?.();
    });
  });

  panel.querySelector("#trApplyAbs")?.addEventListener("click", (ev) => {
    ev.preventDefault();
    ev.stopPropagation();
    const fromEl = panel.querySelector("#trFrom");
    const toEl = panel.querySelector("#trTo");
    if (!fromEl?.value || !toEl?.value) return;
    const fromUtc = new Date(fromEl.value).toISOString();
    const toUtc = new Date(toEl.value).toISOString();
    if (!setAbsoluteRange(fromUtc, toUtc)) return;
    closeTimeRangePanel();
    paintPickerLabel(btn.closest(".tr-picker")?.parentElement || btn.parentElement);
    onChange?.();
  });

  // Don't close when interacting inside the panel (datetime-local UI).
  panel.addEventListener("click", (ev) => ev.stopPropagation());
  panel.addEventListener("mousedown", (ev) => ev.stopPropagation());

  // Position again after layout (fonts / inputs).
  requestAnimationFrame(() => positionPanel(btn, panel));

  // Close on outside click (next tick so this open click is ignored).
  window.setTimeout(() => {
    if (!window.__trOutsideBound) {
      window.__trOutsideBound = true;
      document.addEventListener("click", onDocClickCloseTr);
      window.addEventListener("resize", onViewportChangeTr);
      window.addEventListener("scroll", onViewportChangeTr, true);
      document.addEventListener("keydown", onKeyCloseTr);
    }
  }, 0);
}

function positionPanel(btn, panel) {
  if (!btn || !panel) return;
  const r = btn.getBoundingClientRect();
  const pad = 8;
  // Prefer align right edge of panel with right edge of button (nav is on the right).
  const panelW = panel.offsetWidth || 480;
  const panelH = panel.offsetHeight || 320;
  let left = r.right - panelW;
  let top = r.bottom + 6;
  if (left < pad) left = pad;
  if (left + panelW > window.innerWidth - pad) {
    left = Math.max(pad, window.innerWidth - panelW - pad);
  }
  if (top + panelH > window.innerHeight - pad) {
    // Flip above button if not enough room below.
    top = Math.max(pad, r.top - panelH - 6);
  }
  panel.style.left = `${Math.round(left)}px`;
  panel.style.top = `${Math.round(top)}px`;
}

function closeTimeRangePanel() {
  if (activePanel) {
    activePanel.remove();
    activePanel = null;
  }
  if (activeBtn) {
    activeBtn.setAttribute("aria-expanded", "false");
    activeBtn = null;
  }
}

function onDocClickCloseTr(ev) {
  if (!activePanel) return;
  const t = ev.target;
  if (activePanel.contains(t)) return;
  if (activeBtn && (activeBtn === t || activeBtn.contains(t))) return;
  closeTimeRangePanel();
}

function onViewportChangeTr() {
  if (activePanel && activeBtn) positionPanel(activeBtn, activePanel);
}

function onKeyCloseTr(ev) {
  if (ev.key === "Escape") closeTimeRangePanel();
}

function paintPickerLabel(host) {
  const root = host?.querySelector?.(".tr-btn-label")
    ? host
    : document.getElementById("navTimeRangeHost");
  const lab = root?.querySelector?.(".tr-btn-label");
  if (lab) lab.textContent = getDisplayLabel();
}

/** Re-sync label/options after capability or external state change. */
export function refreshTimeRangePicker(host) {
  if (!host) return;
  const onChange = host._trOnChange;
  mountTimeRangePicker(host, { onChange });
  host._trOnChange = onChange;
}

function toDatetimeLocalValue(d) {
  const pad = (n) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

function escHtml(s) {
  return String(s ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}
