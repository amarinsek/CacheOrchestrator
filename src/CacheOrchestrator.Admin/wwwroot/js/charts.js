/**
 * Lightweight SVG line charts for Metrics time series (no external dependency).
 */

import { esc } from "./format.js";

const SERIES_COLORS = [
  "#3d9cf0",
  "#3ecf8e",
  "#e6b84d",
  "#f07178",
  "#b48ead",
  "#88c0d0",
  "#d08770",
  "#a3be8c",
];

/**
 * Render a multi-series line chart as SVG HTML.
 * @param {Array<{ name: string, points: Array<{ t: number, v: number }> }>} series
 * @param {{ unit?: string, height?: number, width?: number }} [opts]
 */
export function lineChartHtml(series, opts = {}) {
  const height = opts.height || 160;
  const width = opts.width || 560;
  const padL = 44;
  const padR = 10;
  const padT = 12;
  const padB = 22;
  const plotW = width - padL - padR;
  const plotH = height - padT - padB;

  const allPoints = (series || []).flatMap((s) => s.points || []);
  if (!allPoints.length) {
    return `<div class="chart-empty muted">No samples</div>`;
  }

  let tMin = Infinity;
  let tMax = -Infinity;
  let vMin = Infinity;
  let vMax = -Infinity;
  for (const p of allPoints) {
    if (p.t < tMin) tMin = p.t;
    if (p.t > tMax) tMax = p.t;
    if (p.v < vMin) vMin = p.v;
    if (p.v > vMax) vMax = p.v;
  }
  if (tMax <= tMin) tMax = tMin + 1;
  if (vMax <= vMin) {
    vMin = vMin > 0 ? vMin * 0.9 : vMin - 1;
    vMax = vMax < 0 ? vMax * 0.9 : vMax + 1;
  }
  // Percent charts: keep 0–1-ish domain readable
  if (opts.unit === "percent") {
    vMin = Math.min(vMin, 0);
    vMax = Math.max(vMax, Math.min(1, vMax * 1.05 + 0.01));
  } else {
    if (vMin > 0) vMin = 0;
  }

  const xOf = (t) => padL + ((t - tMin) / (tMax - tMin)) * plotW;
  const yOf = (v) => padT + plotH - ((v - vMin) / (vMax - vMin)) * plotH;

  const gridYs = [0, 0.5, 1].map((f) => {
    const v = vMin + (vMax - vMin) * f;
    const y = yOf(v);
    return `<line class="chart-grid" x1="${padL}" y1="${y}" x2="${width - padR}" y2="${y}" />
      <text class="chart-axis" x="${padL - 6}" y="${y + 3}" text-anchor="end">${esc(formatAxis(v, opts.unit))}</text>`;
  }).join("");

  const paths = (series || []).map((s, i) => {
    const pts = s.points || [];
    if (pts.length < 1) return "";
    const d = pts
      .map((p, idx) => `${idx === 0 ? "M" : "L"}${xOf(p.t).toFixed(1)},${yOf(p.v).toFixed(1)}`)
      .join(" ");
    const color = SERIES_COLORS[i % SERIES_COLORS.length];
    return `<path class="chart-line" d="${d}" fill="none" stroke="${color}" stroke-width="1.75" />`;
  }).join("");

  const legend = (series || []).map((s, i) => {
    const color = SERIES_COLORS[i % SERIES_COLORS.length];
    return `<span class="chart-legend-item"><span class="chart-swatch" style="background:${color}"></span>${esc(s.name)}</span>`;
  }).join("");

  return `
    <div class="chart-wrap">
      <svg class="chart-svg" viewBox="0 0 ${width} ${height}" role="img" aria-label="Time series chart">
        ${gridYs}
        ${paths}
      </svg>
      <div class="chart-legend">${legend}</div>
    </div>`;
}

/**
 * Tiny sparkline for Overview embeds.
 * @param {Array<{ t: number, v: number }>} points
 */
export function sparklineHtml(points, opts = {}) {
  const w = opts.width || 120;
  const h = opts.height || 28;
  const pts = points || [];
  if (pts.length < 2) {
    return `<svg class="sparkline" width="${w}" height="${h}"></svg>`;
  }
  let tMin = pts[0].t;
  let tMax = pts[0].t;
  let vMin = pts[0].v;
  let vMax = pts[0].v;
  for (const p of pts) {
    if (p.t < tMin) tMin = p.t;
    if (p.t > tMax) tMax = p.t;
    if (p.v < vMin) vMin = p.v;
    if (p.v > vMax) vMax = p.v;
  }
  if (tMax <= tMin) tMax = tMin + 1;
  if (vMax <= vMin) vMax = vMin + 1;
  const d = pts.map((p, i) => {
    const x = ((p.t - tMin) / (tMax - tMin)) * (w - 2) + 1;
    const y = h - 2 - ((p.v - vMin) / (vMax - vMin)) * (h - 4);
    return `${i === 0 ? "M" : "L"}${x.toFixed(1)},${y.toFixed(1)}`;
  }).join(" ");
  return `<svg class="sparkline" width="${w}" height="${h}" viewBox="0 0 ${w} ${h}">
    <path d="${d}" fill="none" stroke="var(--accent)" stroke-width="1.5" />
  </svg>`;
}

function formatAxis(v, unit) {
  if (unit === "percent") return (v * 100).toFixed(0) + "%";
  if (unit === "ms") return v >= 10 ? v.toFixed(0) : v.toFixed(1);
  if (Math.abs(v) >= 100) return v.toFixed(0);
  if (Math.abs(v) >= 1) return v.toFixed(1);
  if (Math.abs(v) >= 0.01) return v.toFixed(2);
  return v.toExponential(0);
}
