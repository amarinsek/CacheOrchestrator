/**
 * Lightweight SVG line charts for Metrics time series (no external dependency).
 * Modal mode: denser Y grid + mouse snap tooltip along series.
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
 * @param {{ unit?: string, height?: number, width?: number, yTicks?: number, interactive?: boolean }} [opts]
 */
export function lineChartHtml(series, opts = {}) {
  const height = opts.height || 200;
  const width = opts.width || 640;
  const built = buildChartModel(series, opts, width, height);
  if (!built) {
    return `<div class="chart-empty muted">No samples</div>`;
  }
  return chartMarkup(built, opts);
}

/**
 * Soft-refresh helper: update chart DOM without remounting when the fingerprint matches
 * and structure is unchanged (same series count/names). Returns true if updated in place.
 * @param {HTMLElement} host element that currently contains `.chart-wrap` or empty
 * @param {Array} series
 * @param {{ unit?: string, height?: number, width?: number, yTicks?: number, interactive?: boolean }} [opts]
 */
export function updateChartInPlace(host, series, opts = {}) {
  if (!host) return false;
  const height = opts.height || 200;
  const width = opts.width || 640;
  const built = buildChartModel(series, opts, width, height);
  const fp = chartFingerprint(series, opts);

  if (!built) {
    if (host.dataset.chartFp === "empty") return true;
    host.dataset.chartFp = "empty";
    host.innerHTML = `<div class="chart-empty muted">No samples</div>`;
    return true;
  }

  const wrap = host.querySelector?.(":scope > .chart-wrap");
  if (wrap && host.dataset.chartFp === fp) {
    // Identical data — skip DOM work (main source of soft-refresh flicker).
    return true;
  }

  const prevNames = host.dataset.chartSeries || "";
  const nextNames = built.seriesNames.join("\0");
  const svg = wrap?.querySelector("svg.chart-svg");
  const sameLayout = wrap && svg
    && prevNames === nextNames
    && host.dataset.chartUnit === (opts.unit || "")
    && Number(host.dataset.chartYTicks || 3) === built.yTickCount;

  if (sameLayout) {
    // Same series layout: only mutate path `d` and axis labels (no SVG remount).
    const paths = svg.querySelectorAll("path.chart-line");
    built.paths.forEach((d, i) => {
      if (paths[i]) paths[i].setAttribute("d", d);
    });
    const axisTexts = svg.querySelectorAll("text.chart-axis");
    built.axisLabels.forEach((label, i) => {
      if (axisTexts[i]) axisTexts[i].textContent = label;
    });
    const gridLines = svg.querySelectorAll("line.chart-grid");
    built.gridYs.forEach((y, i) => {
      if (gridLines[i]) {
        gridLines[i].setAttribute("y1", String(y));
        gridLines[i].setAttribute("y2", String(y));
      }
    });
    storeInteractionData(wrap, built, series, opts);
    host.dataset.chartFp = fp;
    return true;
  }

  host.dataset.chartFp = fp;
  host.dataset.chartSeries = nextNames;
  host.dataset.chartUnit = opts.unit || "";
  host.dataset.chartYTicks = String(built.yTickCount);
  host.innerHTML = chartMarkup(built, opts);
  const newWrap = host.querySelector(":scope > .chart-wrap");
  if (newWrap) storeInteractionData(newWrap, built, series, opts);
  if (opts.interactive && newWrap) bindChartHover(newWrap);
  return true;
}

/**
 * Modal enlarge for a chart card.
 * @param {{ title: string, series: Array, unit?: string }} opts
 */
export function openChartModal(opts) {
  closeChartModal();
  const backdrop = document.createElement("div");
  backdrop.className = "chart-modal-backdrop";
  backdrop.id = "chartModalBackdrop";
  const series = opts.series || [];
  const unit = opts.unit;
  const chartOpts = {
    unit,
    width: 960,
    height: 420,
    yTicks: 8,
    interactive: true,
  };
  const desc = opts.description && String(opts.description).trim();
  backdrop.innerHTML = `
    <div class="chart-modal" role="dialog" aria-modal="true" aria-label="${esc(opts.title || "Chart")}">
      <div class="chart-modal-head">
        <h2${desc ? ` title="${esc(desc)}"` : ""}>${esc(opts.title || "Chart")}</h2>
        <div class="chart-modal-actions">
          <button type="button" class="secondary chart-modal-icon-btn chart-modal-close" aria-label="Close" title="Close">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><path d="M18 6 6 18"/><path d="m6 6 12 12"/></svg>
          </button>
        </div>
      </div>
      <div class="chart-modal-body">
        ${lineChartHtml(series, chartOpts)}
        <p class="muted small chart-hover-hint">Hover the chart to snap values at a point in time.</p>
      </div>
    </div>`;
  document.body.appendChild(backdrop);
  document.body.classList.add("chart-modal-open");

  const wrap = backdrop.querySelector(".chart-wrap");
  if (wrap) {
    // Rebuild model once so interaction metadata is attached (markup alone has no JS state).
    const built = buildChartModel(series, chartOpts, chartOpts.width, chartOpts.height);
    if (built) {
      storeInteractionData(wrap, built, series, chartOpts);
      bindChartHover(wrap);
    }
  }

  const close = () => closeChartModal();
  backdrop.querySelector(".chart-modal-close")?.addEventListener("click", close);
  backdrop.addEventListener("click", (ev) => {
    if (ev.target === backdrop) close();
  });
  /** @param {KeyboardEvent} ev */
  const onKey = (ev) => {
    if (ev.key === "Escape") close();
  };
  backdrop._onKey = onKey;
  document.addEventListener("keydown", onKey);
}

export function closeChartModal() {
  const el = document.getElementById("chartModalBackdrop");
  if (!el) return;
  if (el._onKey) document.removeEventListener("keydown", el._onKey);
  el.remove();
  document.body.classList.remove("chart-modal-open");
}

/**
 * Bind expand buttons inside a root (delegation-safe rebind).
 * @param {ParentNode} root
 * @param {() => Map<string, { title: string, series: Array, unit?: string }>} getPanelMap
 */
export function bindChartExpand(root, getPanelMap) {
  if (!root || root._chartExpandBound) return;
  root._chartExpandBound = true;
  root.addEventListener("click", (ev) => {
    const btn = ev.target?.closest?.("[data-chart-expand]");
    if (!btn) return;
    const panelId = btn.getAttribute("data-chart-expand");
    if (!panelId) return;
    const map = getPanelMap?.() || new Map();
    const data = map.get(panelId);
    if (!data) return;
    openChartModal(data);
  });
}

function buildChartModel(series, opts, width, height) {
  const interactive = !!opts.interactive;
  const yTickCount = Math.max(2, Math.min(12, opts.yTicks || (interactive ? 8 : 3)));
  const padL = interactive ? 56 : 48;
  const padR = interactive ? 16 : 12;
  const padT = 14;
  const padB = interactive ? 28 : 24;
  const plotW = width - padL - padR;
  const plotH = height - padT - padB;

  const allPoints = (series || []).flatMap((s) => s.points || []);
  if (!allPoints.length) return null;

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
  if (opts.unit === "percent") {
    vMin = Math.min(vMin, 0);
    vMax = Math.max(vMax, Math.min(1, vMax * 1.05 + 0.01));
  } else if (vMin > 0) {
    vMin = 0;
  }

  const xOf = (t) => padL + ((t - tMin) / (tMax - tMin)) * plotW;
  const yOf = (v) => padT + plotH - ((v - vMin) / (vMax - vMin)) * plotH;

  const gridYs = [];
  const axisLabels = [];
  const fractions = [];
  for (let i = 0; i < yTickCount; i++) {
    fractions.push(i / (yTickCount - 1));
  }

  const gridSvg = fractions.map((f) => {
    const v = vMin + (vMax - vMin) * f;
    const y = yOf(v);
    gridYs.push(y);
    const label = formatAxis(v, opts.unit);
    axisLabels.push(label);
    return `<line class="chart-grid" x1="${padL}" y1="${y}" x2="${width - padR}" y2="${y}" />
      <text class="chart-axis" x="${padL - 6}" y="${y + 3}" text-anchor="end">${esc(label)}</text>`;
  }).join("");

  const paths = [];
  const pathSvg = (series || []).map((s, i) => {
    const pts = s.points || [];
    if (pts.length < 1) {
      paths.push("");
      return "";
    }
    const d = pts
      .map((p, idx) => `${idx === 0 ? "M" : "L"}${xOf(p.t).toFixed(1)},${yOf(p.v).toFixed(1)}`)
      .join(" ");
    paths.push(d);
    const color = SERIES_COLORS[i % SERIES_COLORS.length];
    const sw = interactive ? 2.1 : 1.75;
    return `<path class="chart-line" d="${d}" fill="none" stroke="${color}" stroke-width="${sw}" />`;
  }).join("");

  const legend = (series || []).map((s, i) => {
    const color = SERIES_COLORS[i % SERIES_COLORS.length];
    return `<span class="chart-legend-item"><span class="chart-swatch" style="background:${color}"></span>${esc(s.name)}</span>`;
  }).join("");

  return {
    width,
    height,
    padL,
    padR,
    padT,
    padB,
    plotW,
    plotH,
    tMin,
    tMax,
    vMin,
    vMax,
    gridSvg,
    pathSvg,
    legend,
    paths,
    gridYs,
    axisLabels,
    yTickCount,
    seriesNames: (series || []).map((s) => s.name || ""),
    interactive,
  };
}

function chartMarkup(built, opts) {
  const interactive = !!opts.interactive;
  const overlay = interactive
    ? `<g class="chart-hover-layer" pointer-events="none">
        <line class="chart-crosshair" x1="0" y1="${built.padT}" x2="0" y2="${built.height - built.padB}" visibility="hidden" />
        <g class="chart-hover-dots"></g>
      </g>
      <rect class="chart-hit" x="${built.padL}" y="${built.padT}" width="${built.plotW}" height="${built.plotH}"
        fill="transparent" pointer-events="all" />`
    : "";
  const tip = interactive
    ? `<div class="chart-tooltip" hidden></div>`
    : "";
  return `
    <div class="chart-wrap${interactive ? " chart-wrap-interactive" : ""}">
      <svg class="chart-svg" viewBox="0 0 ${built.width} ${built.height}" role="img" aria-label="Time series chart" preserveAspectRatio="xMidYMid meet">
        ${built.gridSvg}
        ${built.pathSvg}
        ${overlay}
      </svg>
      ${tip}
      <div class="chart-legend">${built.legend}</div>
    </div>`;
}

/**
 * @param {HTMLElement} wrap
 * @param {ReturnType<typeof buildChartModel>} built
 * @param {Array} series
 * @param {{ unit?: string }} opts
 */
function storeInteractionData(wrap, built, series, opts) {
  if (!wrap || !built) return;
  wrap._chartIx = {
    built,
    series: (series || []).map((s, i) => ({
      name: s.name || `series ${i + 1}`,
      color: SERIES_COLORS[i % SERIES_COLORS.length],
      points: [...(s.points || [])].sort((a, b) => a.t - b.t),
    })),
    unit: opts.unit || "",
  };
}

/**
 * Mouse snap: value on the drawn polyline at cursor X (linear between samples),
 * with hard snap to a vertex when the cursor is close in plot space (incl. steep slopes).
 * @param {HTMLElement} wrap
 */
function bindChartHover(wrap) {
  if (!wrap || wrap._hoverBound) return;
  wrap._hoverBound = true;

  const svg = wrap.querySelector("svg.chart-svg");
  const hit = svg?.querySelector(".chart-hit");
  const cross = svg?.querySelector(".chart-crosshair");
  const dotsG = svg?.querySelector(".chart-hover-dots");
  const tip = wrap.querySelector(".chart-tooltip");
  if (!svg || !hit || !cross || !dotsG || !tip) return;

  const hide = () => {
    cross.setAttribute("visibility", "hidden");
    dotsG.innerHTML = "";
    tip.hidden = true;
  };

  hit.addEventListener("mouseleave", hide);
  hit.addEventListener("mousemove", (ev) => {
    const ix = wrap._chartIx;
    if (!ix?.built) return;
    const { built, series, unit } = ix;
    const pt = clientToSvg(svg, ev.clientX, ev.clientY);
    if (!pt) return;

    // Prefer geometric snap to a nearby vertex (works on steep / long slopes),
    // else sample the polyline at cursor X so the marker sits on the segment.
    const vertexSnap = nearestVertexAcrossSeries(series, built, pt.x, pt.y, /*maxPx*/ 14);
    let snapX;
    let snapT;
    if (vertexSnap) {
      snapX = vertexSnap.x;
      snapT = vertexSnap.t;
    } else {
      snapX = Math.min(built.padL + built.plotW, Math.max(built.padL, pt.x));
      snapT = tAtX(built, snapX);
    }

    /** @type {Array<{ name: string, color: string, t: number, v: number, x: number, y: number }>} */
    const hits = [];
    for (const s of series) {
      const onLine = samplePolylineAtT(s.points, built, snapT);
      if (!onLine) continue;
      hits.push({
        name: s.name,
        color: s.color,
        t: onLine.t,
        v: onLine.v,
        x: onLine.x,
        y: onLine.y,
      });
    }
    if (!hits.length) {
      hide();
      return;
    }

    // Shared vertical at snapped time (aligned to sample if vertex snap, else cursor X).
    cross.setAttribute("x1", String(snapX));
    cross.setAttribute("x2", String(snapX));
    cross.setAttribute("visibility", "visible");

    dotsG.innerHTML = hits.map((h) =>
      `<circle cx="${h.x.toFixed(1)}" cy="${h.y.toFixed(1)}" r="4.5" fill="${h.color}" stroke="#0d1218" stroke-width="1.5" />`
    ).join("");

    const timeLabel = formatTime(snapT);
    const rows = hits.map((h) =>
      `<div class="chart-tip-row">
        <span class="chart-tip-swatch" style="background:${h.color}"></span>
        <span class="chart-tip-name">${esc(h.name)}</span>
        <span class="chart-tip-val">${esc(formatValue(h.v, unit))}</span>
      </div>`
    ).join("");
    tip.innerHTML = `<div class="chart-tip-time">${esc(timeLabel)}</div>${rows}`;
    tip.hidden = false;

    // Position tooltip near cursor, keep inside wrap.
    const wrapRect = wrap.getBoundingClientRect();
    const svgRect = svg.getBoundingClientRect();
    const anchorY = hits.reduce((sum, h) => sum + h.y, 0) / hits.length;
    const relX = ((snapX / built.width) * svgRect.width) + (svgRect.left - wrapRect.left);
    const relY = ((anchorY / built.height) * svgRect.height) + (svgRect.top - wrapRect.top);
    const tipW = tip.offsetWidth || 160;
    const tipH = tip.offsetHeight || 60;
    let left = relX + 14;
    let top = relY - tipH / 2;
    if (left + tipW > wrapRect.width - 4) left = relX - tipW - 14;
    if (left < 4) left = 4;
    if (top < 4) top = 4;
    if (top + tipH > wrapRect.height - 4) top = Math.max(4, wrapRect.height - tipH - 4);
    tip.style.left = `${left}px`;
    tip.style.top = `${top}px`;
  });
}

/** @param {SVGSVGElement} svg @param {number} clientX @param {number} clientY */
function clientToSvg(svg, clientX, clientY) {
  const ctm = svg.getScreenCTM();
  if (!ctm) return null;
  const pt = svg.createSVGPoint();
  pt.x = clientX;
  pt.y = clientY;
  const inv = ctm.inverse();
  const p = pt.matrixTransform(inv);
  return { x: p.x, y: p.y };
}

function xOfT(built, t) {
  return built.padL + ((t - built.tMin) / (built.tMax - built.tMin)) * built.plotW;
}

function yOfV(built, v) {
  return built.padT + built.plotH - ((v - built.vMin) / (built.vMax - built.vMin)) * built.plotH;
}

function tAtX(built, x) {
  return built.tMin + ((x - built.padL) / built.plotW) * (built.tMax - built.tMin);
}

/**
 * Value on the drawn polyline at time t (linear between samples).
 * Marker sits on sloped segments, not only at endpoints.
 * @param {Array<{t:number,v:number}>} points
 * @param {*} built
 * @param {number} t
 */
function samplePolylineAtT(points, built, t) {
  if (!points?.length) return null;
  if (points.length === 1 || t <= points[0].t) {
    const p = points[0];
    return { t: p.t, v: p.v, x: xOfT(built, p.t), y: yOfV(built, p.v) };
  }
  const last = points[points.length - 1];
  if (t >= last.t) {
    return { t: last.t, v: last.v, x: xOfT(built, last.t), y: yOfV(built, last.v) };
  }

  // Binary search segment
  let lo = 0;
  let hi = points.length - 1;
  while (hi - lo > 1) {
    const mid = (lo + hi) >> 1;
    if (points[mid].t <= t) lo = mid;
    else hi = mid;
  }
  const a = points[lo];
  const b = points[hi];
  const span = b.t - a.t;
  const u = span === 0 ? 0 : (t - a.t) / span;
  const v = a.v + (b.v - a.v) * u;
  // Place marker on the segment using the same linear mapping as the path (t → x).
  const x = xOfT(built, t);
  const y = yOfV(built, v);
  return { t, v, x, y };
}

/**
 * Nearest sample vertex across all series within maxPx of the cursor (plot space).
 * Uses Euclidean distance so steep slopes still snap to the intended sample.
 * @param {Array<{name:string,color:string,points:Array}>} series
 * @param {*} built
 * @param {number} mx
 * @param {number} my
 * @param {number} maxPx
 */
function nearestVertexAcrossSeries(series, built, mx, my, maxPx) {
  let best = null;
  let bestD2 = maxPx * maxPx;
  for (const s of series) {
    for (const p of s.points || []) {
      const x = xOfT(built, p.t);
      const y = yOfV(built, p.v);
      const dx = x - mx;
      const dy = y - my;
      const d2 = dx * dx + dy * dy;
      if (d2 <= bestD2) {
        bestD2 = d2;
        best = { t: p.t, v: p.v, x, y };
      }
    }
  }
  return best;
}

function chartFingerprint(series, opts) {
  const parts = [opts.unit || "", String(opts.yTicks || ""), opts.interactive ? "1" : "0"];
  for (const s of series || []) {
    parts.push(s.name || "");
    for (const p of s.points || []) {
      parts.push(String(p.t), String(p.v));
    }
  }
  return parts.join("|");
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

function formatValue(v, unit) {
  if (v == null || Number.isNaN(v)) return "—";
  if (unit === "percent") return (v * 100).toFixed(1) + "%";
  if (unit === "ms") return (Math.abs(v) >= 10 ? v.toFixed(1) : v.toFixed(2)) + " ms";
  if (unit === "rate") {
    if (Math.abs(v) >= 100) return v.toFixed(1) + "/s";
    if (Math.abs(v) >= 1) return v.toFixed(2) + "/s";
    if (Math.abs(v) >= 0.01) return v.toFixed(3) + "/s";
    return v.toExponential(1) + "/s";
  }
  if (Math.abs(v) >= 100) return v.toFixed(1);
  if (Math.abs(v) >= 1) return v.toFixed(2);
  if (Math.abs(v) >= 0.01) return v.toFixed(3);
  return v.toExponential(1);
}

/** Prometheus samples are usually unix seconds. */
function formatTime(t) {
  if (t == null || Number.isNaN(t)) return "—";
  // Heuristic: ms timestamps are huge
  const ms = t > 1e12 ? t : t * 1000;
  try {
    return new Date(ms).toLocaleString(undefined, {
      month: "short",
      day: "numeric",
      hour: "2-digit",
      minute: "2-digit",
      second: "2-digit",
    });
  } catch {
    return String(t);
  }
}
