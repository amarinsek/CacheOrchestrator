/**
 * Display formatting helpers (HTML-safe strings, percentages, units, pipeline bar).
 *
 * Unit convention: thin space (U+2009) between number and unit — e.g. "5 m", "11 ms".
 * Pure counts stay locale-formatted numbers without a unit suffix.
 */

/** Escape text for safe interpolation into HTML templates. */
export function esc(s) {
  return String(s ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

/**
 * Format a 0–1 rate as a percentage string.
 * When `lowSample` is true, wraps the value with a dashed underline and title.
 */
export function pct(rate, lowSample) {
  if (rate == null || Number.isNaN(rate)) return "—";
  const s = (rate * 100).toFixed(1) + "%";
  return lowSample
    ? `<span class="low-n" title="Low sample (layer n &lt; 20)">${s}</span>`
    : s;
}

/** Locale-aware integer/number formatting; null → em dash. */
export function num(n) {
  if (n == null) return "—";
  return Number(n).toLocaleString();
}

/**
 * Horizontal request-pipeline share bar (OC hit · FC hit · Origin · Bypass · Other).
 * @param {object|null} p Pipeline DTO with *Share fields
 * @param {boolean} [large] Wider/taller bar for detail pages
 */
export function pipelineBar(p, large) {
  if (!p) return `<div class="pipe empty"></div>`;
  const parts = [
    ["oc", p.ocHitShare, "OC hit"],
    ["fc", p.fcHitShare, "FC hit"],
    ["origin", p.originShare, "Origin (factory)"],
    ["bypass", p.bypassShare, "Bypass"],
    ["other", p.otherShare, "Other"],
  ].filter(([, v]) => v != null && v > 0.0005);
  if (!parts.length) return `<div class="pipe empty${large ? " lg" : ""}"></div>`;
  return `<div class="pipe${large ? " lg" : ""}">${
    parts.map(([cls, v, label]) =>
      `<span class="seg ${cls}" style="flex:${Math.max(v, 0.01)}" title="${label}: ${(v * 100).toFixed(1)}%"></span>`
    ).join("")
  }</div>`;
}

/** Min–max–mean share cell when an entity is split across instances. */
export function spreadCell(s) {
  if (!s || s.sampleCount < 1) return "—";
  if (s.sampleCount === 1) return pct(s.mean);
  return `${pct(s.min)}–${pct(s.max)} <span class="muted">μ ${pct(s.mean)}</span>`;
}

/**
 * Number + unit with thin space. Accepts raw numbers (locale-formatted)
 * or an already-safe display string.
 */
export function fmtUnit(value, unit) {
  if (value == null || value === "") return "—";
  if (typeof value === "number") {
    if (Number.isNaN(value)) return "—";
    return `${num(value)}\u2009${unit}`;
  }
  const s = String(value).trim();
  if (!s || s === "—") return "—";
  return `${s}\u2009${unit}`;
}

/** Human uptime from whole seconds: "3 h 12 m", "45 s", etc. */
export function formatUptime(seconds) {
  if (seconds == null || seconds < 0 || Number.isNaN(Number(seconds))) return "—";
  const s = Math.floor(Number(seconds));
  const d = Math.floor(s / 86400);
  const h = Math.floor((s % 86400) / 3600);
  const m = Math.floor((s % 3600) / 60);
  const sec = s % 60;
  if (d > 0) return `${fmtUnit(d, "d")} ${fmtUnit(h, "h")}`;
  if (h > 0) return `${fmtUnit(h, "h")} ${fmtUnit(m, "m")}`;
  if (m > 0) return `${fmtUnit(m, "m")} ${fmtUnit(sec, "s")}`;
  return fmtUnit(sec, "s");
}

/** Round-trip latency for health probes. */
export function formatLatencyMs(ms) {
  if (ms == null || Number.isNaN(Number(ms))) return "—";
  return fmtUnit(Math.round(Number(ms)), "ms");
}

/** Compact duration labels for chrome (refresh interval): "5 s", "1 m". */
export function formatDurationLabel(seconds) {
  if (seconds == null || Number.isNaN(Number(seconds))) return "—";
  const s = Number(seconds);
  if (s >= 60 && s % 60 === 0) return fmtUnit(s / 60, "m");
  return fmtUnit(s, "s");
}
