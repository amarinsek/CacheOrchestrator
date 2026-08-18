/**
 * Global stats time range for Admin Console (Grafana-style).
 * All traffic stats require Prometheus (Metrics store Connected).
 * Relative Last N or absolute from/to UTC.
 */

const STORAGE_KEY = "adminStatsTimeRange";

/**
 * @typedef {{
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

/** Calendar-day quick ranges (local timezone). Applied as absolute from/to. */
export const CALENDAR_SHORTCUTS = [
  { id: "today", label: "Today" },
  { id: "yesterday", label: "Yesterday" },
  { id: "day-before-yesterday", label: "Day before yesterday" },
];

export const CUSTOM_RANGE_ID = "custom";

const CLOCK_ICON_SVG = `<svg class="tr-btn-icon" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/></svg>`;

/** @type {TimeRangeState} */
let state = load();

/** @type {Set<() => void>} */
const listeners = new Set();

/** @type {"unknown"|"connected"|"disconnected"|"not_configured"} */
let metricsCapability = "unknown";

function load() {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return { mode: "windowed", range: "1h", fromUtc: null, toUtc: null };
    const o = JSON.parse(raw);
    // Migrate away from removed process_totals mode.
    if (o?.mode === "process_totals" || o?.mode === "process") {
      return { mode: "windowed", range: "1h", fromUtc: null, toUtc: null };
    }
    if (o?.mode === "windowed" || o?.range) {
      if (o.range === CUSTOM_RANGE_ID && o.fromUtc && o.toUtc) {
        return { mode: "windowed", range: CUSTOM_RANGE_ID, fromUtc: o.fromUtc, toUtc: o.toUtc };
      }
      return {
        mode: "windowed",
        range: normalizeRange(o.range || "1h"),
        fromUtc: null,
        toUtc: null,
      };
    }
  } catch { /* ignore */ }
  return { mode: "windowed", range: "1h", fromUtc: null, toUtc: null };
}

function persist() {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(state));
  } catch { /* ignore */ }
}

export function normalizeRange(range) {
  const id = String(range || "1h").trim().toLowerCase();
  if (id === CUSTOM_RANGE_ID) return CUSTOM_RANGE_ID;
  if (id === "process_totals" || id === "process" || id === "lifetime") return "1h";
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

export function getAbsoluteWindow() {
  if (state.range !== CUSTOM_RANGE_ID || !state.fromUtc || !state.toUtc) return null;
  const tMin = Math.floor(new Date(state.fromUtc).getTime() / 1000);
  const tMax = Math.floor(new Date(state.toUtc).getTime() / 1000);
  if (!Number.isFinite(tMin) || !Number.isFinite(tMax) || tMax <= tMin) return null;
  return { tMin, tMax, fromUtc: state.fromUtc, toUtc: state.toUtc };
}

export function getTimeRange() {
  return { ...state };
}

/** True when Metrics store is Connected (stats UI can load). */
export function isMetricsConnected() {
  return metricsCapability === "connected";
}

/** @deprecated Use isMetricsConnected — process totals removed. */
export function isProcessTotals() {
  return !isMetricsConnected();
}

/** @deprecated Use isMetricsConnected */
export function isWindowedEffective() {
  return isMetricsConnected();
}

export function isCustomAbsolute() {
  return isMetricsConnected() && state.range === CUSTOM_RANGE_ID && !!getAbsoluteWindow();
}

export function getPromRange() {
  if (!isMetricsConnected()) return null;
  if (state.range === CUSTOM_RANGE_ID) return null;
  return normalizeRange(state.range || "1h");
}

export function getMetricsQueryArgs() {
  if (isCustomAbsolute()) {
    const abs = getAbsoluteWindow();
    return { range: CUSTOM_RANGE_ID, from: abs.fromUtc, to: abs.toUtc };
  }
  return { range: normalizeRange(state.range || "1h") };
}

export function appendMetricsRangeParams(q) {
  const a = getMetricsQueryArgs();
  if (a.range) q.set("range", a.range);
  if (a.from) q.set("from", a.from);
  if (a.to) q.set("to", a.to);
  return q;
}

export function getDisplayLabel() {
  if (state.range === CUSTOM_RANGE_ID && state.fromUtc && state.toUtc) {
    const cal = matchCalendarShortcut(state.fromUtc, state.toUtc);
    if (cal) return cal.label;
    return formatAbsoluteLabel(state.fromUtc, state.toUtc);
  }
  const id = normalizeRange(state.range || "1h");
  return WINDOW_SHORTCUTS.find((s) => s.id === id)?.label || `Last ${id}`;
}

export function getBadgeText() {
  if (state.range === CUSTOM_RANGE_ID && state.fromUtc && state.toUtc) return "Custom";
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
      range: normalizeRange(next.range || next.mode || "1h"),
      fromUtc: null,
      toUtc: null,
    };
  }
  persist();
  notify();
}

export function getSelectValue() {
  if (state.range === CUSTOM_RANGE_ID) return CUSTOM_RANGE_ID;
  return normalizeRange(state.range || "1h");
}

export function setFromSelectValue(value) {
  const v = String(value || "").trim();
  if (v === CUSTOM_RANGE_ID) return;
  if (v === "process_totals" || v === "lifetime" || v === "process") {
    setTimeRange({ range: "1h" });
    return;
  }
  setTimeRange({ range: normalizeRange(v) });
}

export function setAbsoluteRange(fromUtc, toUtc) {
  const f = new Date(fromUtc);
  const t = new Date(toUtc);
  if (Number.isNaN(f.getTime()) || Number.isNaN(t.getTime()) || t <= f) return false;
  setTimeRange({
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
  const cur = getSelectValue();
  return WINDOW_SHORTCUTS.map((s) =>
    `<option value="${s.id}"${cur === s.id ? " selected" : ""}>${s.label}</option>`).join("");
}

// —— Grafana-style picker ——

/** @type {HTMLElement|null} */
let activePanel = null;
/** @type {HTMLElement|null} */
let activeBtn = null;

export function mountTimeRangePicker(host, opts = {}) {
  if (!host) return;
  closeTimeRangePanel();
  host.innerHTML = pickerButtonHtml();
  bindPicker(host, opts.onChange);
}

function pickerButtonHtml() {
  const connected = isMetricsConnected() || metricsCapability === "unknown";
  const label = getDisplayLabel();
  const title = connected
    ? "Time range for statistics and charts"
    : "Connect metrics to use statistics and charts";
  return `
    <div class="tr-picker" title="${escHtml(title)}">
      <button type="button" class="tr-btn" id="trPickerBtn" aria-haspopup="dialog" aria-expanded="false"
        ${connected ? "" : "disabled"}>
        ${CLOCK_ICON_SVG}
        <span class="tr-btn-label">${escHtml(connected ? label : "Metrics offline")}</span>
        <span class="tr-btn-caret" aria-hidden="true">▾</span>
      </button>
    </div>`;
}

function pickerPanelHtml() {
  const connected = isMetricsConnected() || metricsCapability === "unknown";
  const cur = getSelectValue();
  const activeCal = (state.range === CUSTOM_RANGE_ID && state.fromUtc && state.toUtc)
    ? matchCalendarShortcut(state.fromUtc, state.toUtc)?.id
    : null;

  const calBtns = CALENDAR_SHORTCUTS.map((s) => {
    const active = activeCal === s.id ? " active" : "";
    const dis = connected ? "" : " disabled";
    return `<button type="button" class="tr-rel-btn${active}" data-tr-cal="${s.id}"${dis}>${escHtml(s.label)}</button>`;
  }).join("");

  const relBtns = WINDOW_SHORTCUTS.map((s) => {
    const active = cur === s.id ? " active" : "";
    const dis = connected ? "" : " disabled";
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
      <div class="tr-col tr-abs">
        <div class="tr-col-title">Absolute time range</div>
        <label class="tr-field">From
          <input type="datetime-local" id="trFrom" value="${escHtml(fromLocal)}" ${connected ? "" : "disabled"} step="60" />
        </label>
        <label class="tr-field">To
          <input type="datetime-local" id="trTo" value="${escHtml(toLocal)}" ${connected ? "" : "disabled"} step="60" />
        </label>
        <p class="tr-abs-error" id="trAbsError" hidden>To must be after From.</p>
        <button type="button" class="tr-apply" id="trApplyAbs" ${connected ? "" : "disabled"}>Apply time range</button>
        <p class="tr-hint">Statistics and charts use this time range. Live uses a separate short lookback.</p>
      </div>
      <div class="tr-col tr-rel">
        <div class="tr-col-title">Quick ranges</div>
        ${calBtns}
        <div class="tr-rel-sep" aria-hidden="true"></div>
        ${relBtns}
      </div>
    </div>`;
}

function bindPicker(host, onChange) {
  const btn = host.querySelector("#trPickerBtn");
  if (!btn || btn.disabled) return;

  btn.addEventListener("click", (ev) => {
    ev.preventDefault();
    ev.stopPropagation();
    if (activePanel && activeBtn === btn) {
      closeTimeRangePanel();
      return;
    }
    openTimeRangePanel(btn, onChange);
  });
}

function openTimeRangePanel(btn, onChange) {
  closeTimeRangePanel();
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
      paintPickerLabel();
      onChange?.();
    });
  });

  panel.querySelectorAll("[data-tr-cal]").forEach((b) => {
    b.addEventListener("click", (ev) => {
      ev.preventDefault();
      ev.stopPropagation();
      if (b.disabled) return;
      const id = b.getAttribute("data-tr-cal");
      const win = calendarRangeLocal(id);
      if (!win) return;
      if (!setAbsoluteRange(win.from.toISOString(), win.to.toISOString())) return;
      closeTimeRangePanel();
      paintPickerLabel();
      onChange?.();
    });
  });

  const fromEl = panel.querySelector("#trFrom");
  const toEl = panel.querySelector("#trTo");
  const syncAbs = () => updateAbsoluteValidity(panel);
  fromEl?.addEventListener("input", syncAbs);
  toEl?.addEventListener("input", syncAbs);
  fromEl?.addEventListener("change", syncAbs);
  toEl?.addEventListener("change", syncAbs);
  updateAbsoluteValidity(panel);

  panel.querySelector("#trApplyAbs")?.addEventListener("click", (ev) => {
    ev.preventDefault();
    ev.stopPropagation();
    if (!updateAbsoluteValidity(panel)) return;
    if (!fromEl?.value || !toEl?.value) return;
    if (!setAbsoluteRange(new Date(fromEl.value).toISOString(), new Date(toEl.value).toISOString())) return;
    closeTimeRangePanel();
    paintPickerLabel();
    onChange?.();
  });

  panel.addEventListener("click", (ev) => ev.stopPropagation());
  panel.addEventListener("mousedown", (ev) => ev.stopPropagation());
  requestAnimationFrame(() => positionPanel(btn, panel));

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
  const panelW = panel.offsetWidth || 480;
  const panelH = panel.offsetHeight || 320;
  let left = r.right - panelW;
  let top = r.bottom + 6;
  if (left < pad) left = pad;
  if (left + panelW > window.innerWidth - pad) left = Math.max(pad, window.innerWidth - panelW - pad);
  if (top + panelH > window.innerHeight - pad) top = Math.max(pad, r.top - panelH - 6);
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
  if (activePanel.contains(ev.target)) return;
  if (activeBtn && (activeBtn === ev.target || activeBtn.contains(ev.target))) return;
  closeTimeRangePanel();
}

function onViewportChangeTr() {
  if (activePanel && activeBtn) positionPanel(activeBtn, activePanel);
}

function onKeyCloseTr(ev) {
  if (ev.key === "Escape") closeTimeRangePanel();
}

function paintPickerLabel() {
  const lab = document.querySelector("#navTimeRangeHost .tr-btn-label");
  if (lab) lab.textContent = isMetricsConnected() || metricsCapability === "unknown"
    ? getDisplayLabel()
    : "Metrics offline";
}

export function refreshTimeRangePicker(host) {
  if (!host) return;
  const onChange = host._trOnChange;
  mountTimeRangePicker(host, { onChange });
  host._trOnChange = onChange;
  if (host.classList.contains("is-disabled")) {
    applyTimeRangeDisabledState(host, host._trDisabledReason || "");
  }
}

/**
 * Enable/disable the nav Range picker (Live page locks it — lookback is always 1m).
 * @param {boolean} enabled
 * @param {{ reason?: string }} [opts]
 */
export function setTimeRangeControlsEnabled(enabled, opts = {}) {
  const host = document.getElementById("navTimeRangeHost");
  if (!host) return;
  if (!enabled) {
    closeTimeRangePanel();
    host._trDisabledReason = opts.reason || "Time range is fixed on Live (1m lookback)";
    host.classList.add("is-disabled");
    applyTimeRangeDisabledState(host, host._trDisabledReason);
    return;
  }
  host.classList.remove("is-disabled");
  host._trDisabledReason = "";
  host.removeAttribute("title");
  refreshTimeRangePicker(host);
}

function applyTimeRangeDisabledState(host, reason) {
  host.title = reason || "Time range unavailable";
  const btn = host.querySelector("#trPickerBtn");
  if (!btn) return;
  btn.disabled = true;
  btn.setAttribute("aria-expanded", "false");
  btn.title = reason || host.title;
}

function toDatetimeLocalValue(d) {
  const pad = (n) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

/** Local calendar midnight for the given date. */
function startOfLocalDay(d) {
  return new Date(d.getFullYear(), d.getMonth(), d.getDate(), 0, 0, 0, 0);
}

/**
 * Absolute window for a calendar quick range (local timezone).
 * Today = midnight → now; Yesterday / day-before = full calendar day [00:00, next midnight).
 * @param {string|null} id
 * @returns {{ from: Date, to: Date }|null}
 */
export function calendarRangeLocal(id) {
  const now = new Date();
  const todayStart = startOfLocalDay(now);
  if (id === "today") {
    return { from: todayStart, to: now };
  }
  if (id === "yesterday") {
    const from = new Date(todayStart);
    from.setDate(from.getDate() - 1);
    return { from, to: todayStart };
  }
  if (id === "day-before-yesterday") {
    const to = new Date(todayStart);
    to.setDate(to.getDate() - 1);
    const from = new Date(todayStart);
    from.setDate(from.getDate() - 2);
    return { from, to };
  }
  return null;
}

/**
 * If the absolute window matches a calendar shortcut, return it.
 * Today: from ≈ local midnight today and to still on today's calendar day.
 * Yesterday / day-before: full local day bounds (with small slack).
 * @param {string} fromUtc
 * @param {string} toUtc
 */
function matchCalendarShortcut(fromUtc, toUtc) {
  const fromMs = new Date(fromUtc).getTime();
  const toMs = new Date(toUtc).getTime();
  if (!Number.isFinite(fromMs) || !Number.isFinite(toMs) || toMs <= fromMs) return null;
  const slackMs = 90_000;
  const now = new Date();
  const todayStart = startOfLocalDay(now);

  if (Math.abs(todayStart.getTime() - fromMs) <= slackMs
    && startOfLocalDay(new Date(toMs)).getTime() === todayStart.getTime()) {
    return CALENDAR_SHORTCUTS.find((s) => s.id === "today") || null;
  }

  for (const s of CALENDAR_SHORTCUTS) {
    if (s.id === "today") continue;
    const win = calendarRangeLocal(s.id);
    if (!win) continue;
    if (Math.abs(win.from.getTime() - fromMs) <= slackMs
      && Math.abs(win.to.getTime() - toMs) <= slackMs) {
      return s;
    }
  }
  return null;
}

/**
 * Validates absolute From/To in the open panel. Shows error + invalid styling when To ≤ From.
 * @param {HTMLElement} panel
 * @returns {boolean} true when range is valid (or incomplete / offline)
 */
function updateAbsoluteValidity(panel) {
  const fromEl = panel.querySelector("#trFrom");
  const toEl = panel.querySelector("#trTo");
  const errEl = panel.querySelector("#trAbsError");
  const applyBtn = panel.querySelector("#trApplyAbs");
  const connected = isMetricsConnected() || metricsCapability === "unknown";

  const clearInvalid = () => {
    fromEl?.classList.remove("invalid");
    toEl?.classList.remove("invalid");
    if (errEl) errEl.hidden = true;
  };

  if (!connected || !fromEl || !toEl) {
    clearInvalid();
    if (applyBtn) applyBtn.disabled = !connected;
    return false;
  }

  if (!fromEl.value || !toEl.value) {
    clearInvalid();
    if (applyBtn) applyBtn.disabled = true;
    return false;
  }

  const from = new Date(fromEl.value);
  const to = new Date(toEl.value);
  const invalid = Number.isNaN(from.getTime()) || Number.isNaN(to.getTime()) || to <= from;

  if (invalid) {
    fromEl.classList.add("invalid");
    toEl.classList.add("invalid");
    if (errEl) {
      errEl.hidden = false;
      errEl.textContent = to <= from && !Number.isNaN(from.getTime()) && !Number.isNaN(to.getTime())
        ? "To must be after From."
        : "Enter a valid From and To.";
    }
    if (applyBtn) applyBtn.disabled = true;
    return false;
  }

  clearInvalid();
  if (applyBtn) applyBtn.disabled = false;
  return true;
}

function escHtml(s) {
  return String(s ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}
