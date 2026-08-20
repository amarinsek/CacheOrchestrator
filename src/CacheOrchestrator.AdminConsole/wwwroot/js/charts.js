/**
 * Lightweight SVG line charts for Metrics time series (no external dependency).
 * Modal mode: denser Y grid + mouse snap tooltip along series.
 */

import { esc } from "./format.js";

/** Tooltip when a panel has no Prometheus samples in the window. */
const NO_SAMPLES_TIP =
  "No data in this time range. Often the event rate was simply zero.";

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
 * True when series has at least one sample point.
 * @param {Array<{ points?: Array }>|null|undefined} series
 */
export function seriesHasSamples(series) {
  return (series || []).some((s) => (s.points || []).length > 0);
}

/** Card-chart viewBox. CSS `aspect-ratio` on `.chart-svg` must match (no JS resize). */
export const CARD_CHART_WIDTH = 640;
export const CARD_CHART_HEIGHT = 240;

/**
 * Render a multi-series line chart as SVG HTML.
 * When the selected time window is known (tMin/tMax) but there are no points,
 * draws empty axes for that window (zero baseline) instead of a "No samples" box.
 * @param {Array<{ name: string, points: Array<{ t: number, v: number }> }>} series
 * @param {{ unit?: string, height?: number, width?: number, yTicks?: number, interactive?: boolean, tMin?: number, tMax?: number, range?: string, step?: string|null, stepSec?: number|null, lookbackMarkerSec?: number|null }} [opts]
 */
export function lineChartHtml(series, opts = {}) {
  const height = opts.height || CARD_CHART_HEIGHT;
  const width = opts.width || CARD_CHART_WIDTH;
  const built = buildChartModel(series, opts, width, height);
  if (!built) {
    return `<div class="chart-empty muted">No time window</div>`;
  }
  return chartMarkup(built, opts);
}

/**
 * Soft-refresh helper: update chart DOM without remounting when the fingerprint matches
 * and structure is unchanged (same series count/names). Returns true if updated in place.
 * @param {HTMLElement} host element that currently contains `.chart-wrap` or empty
 * @param {Array} series
 * @param {{ unit?: string, height?: number, width?: number, yTicks?: number, interactive?: boolean, tMin?: number, tMax?: number, range?: string, step?: string|null, stepSec?: number|null, lookbackMarkerSec?: number|null }} [opts]
 */
export function updateChartInPlace(host, series, opts = {}) {
  if (!host) return false;
  const height = opts.height || CARD_CHART_HEIGHT;
  const width = opts.width || CARD_CHART_WIDTH;
  const built = buildChartModel(series, opts, width, height);
  const fp = chartFingerprint(series, opts);

  if (!built) {
    if (host.dataset.chartFp === "nowindow" && host.dataset.chartRange === (opts.range || "")) return true;
    host.dataset.chartFp = "nowindow";
    host.dataset.chartRange = opts.range || "";
    host.innerHTML = `<div class="chart-empty muted">No time window</div>`;
    return true;
  }

  const wrap = host.querySelector?.(":scope > .chart-wrap");
  if (wrap && host.dataset.chartFp === fp) {
    // Identical data — skip DOM work (main source of soft-refresh flicker).
    return true;
  }

  // Range / empty↔data / window change always remounts so axes match the selected time window.
  const rangeChanged = (host.dataset.chartRange || "") !== (opts.range || "")
    || host.dataset.chartTMin !== String(opts.tMin ?? "")
    || host.dataset.chartTMax !== String(opts.tMax ?? "")
    || host.dataset.chartEmpty !== (built.empty ? "1" : "0");

  const prevNames = host.dataset.chartSeries || "";
  const nextNames = built.seriesNames.join("\0");
  const svg = wrap?.querySelector("svg.chart-svg");
  const sameLayout = !rangeChanged && wrap && svg
    && prevNames === nextNames
    && host.dataset.chartUnit === (opts.unit || "")
    && Number(host.dataset.chartYTicks || 3) === built.yTickCount
    && Number(host.dataset.chartXTicks || 0) === built.xTickCount
    && Number(host.dataset.chartMarker || 0) === Number(opts.lookbackMarkerSec || 0);

  if (sameLayout) {
    // Same series layout: only mutate path `d` and axis labels (no SVG remount).
    const paths = svg.querySelectorAll("path.chart-line");
    built.paths.forEach((d, i) => {
      if (paths[i]) paths[i].setAttribute("d", d);
    });
    const axisY = svg.querySelectorAll("text.chart-axis-y");
    built.axisLabels.forEach((label, i) => {
      if (axisY[i]) axisY[i].textContent = label;
    });
    const hGrid = svg.querySelectorAll("line.chart-grid-h");
    built.gridYs.forEach((y, i) => {
      if (hGrid[i]) {
        hGrid[i].setAttribute("y1", String(y));
        hGrid[i].setAttribute("y2", String(y));
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
  host.dataset.chartXTicks = String(built.xTickCount);
  host.dataset.chartRange = opts.range || "";
  host.dataset.chartTMin = String(opts.tMin ?? "");
  host.dataset.chartTMax = String(opts.tMax ?? "");
  host.dataset.chartEmpty = built.empty ? "1" : "0";
  host.dataset.chartMarker = String(opts.lookbackMarkerSec || 0);
  host.innerHTML = chartMarkup(built, opts);
  const newWrap = host.querySelector(":scope > .chart-wrap");
  if (newWrap) storeInteractionData(newWrap, built, series, opts);
  if (opts.interactive && newWrap) bindChartHover(newWrap);
  return true;
}

/** @type {{ panelId: string, getPanelMap: () => Map<string, any> }|null} */
let openModalCtx = null;

/**
 * Modal enlarge for a chart card.
 * @param {{ title: string, series: Array, unit?: string, range?: string, tMin?: number, tMax?: number, panelId?: string, description?: string }} opts
 * @param {{ getPanelMap?: () => Map<string, any> }} [ctx]
 */
export function openChartModal(opts, ctx = {}) {
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
    range: opts.range,
    tMin: opts.tMin,
    tMax: opts.tMax,
    step: opts.step,
    lookbackMarkerSec: opts.lookbackMarkerSec,
  };
  const desc = opts.description && String(opts.description).trim();
  backdrop.innerHTML = `
    <div class="chart-modal" role="dialog" aria-modal="true" aria-label="${esc(opts.title || "Chart")}">
      <div class="chart-modal-head">
        <h2${desc ? ` title="${esc(desc)}"` : ""}>${esc(opts.title || "Chart")}</h2>
        <div class="chart-modal-actions">
          <button type="button" class="secondary chart-modal-icon-btn" id="chartModalRefresh" aria-label="Refresh" title="Refresh all (same as menu ↻)">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M21 12a9 9 0 1 1-2.64-6.36"/><path d="M21 3v6h-6"/></svg>
          </button>
          <button type="button" class="secondary chart-modal-icon-btn chart-modal-close" aria-label="Close" title="Close">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><path d="M18 6 6 18"/><path d="m6 6 12 12"/></svg>
          </button>
        </div>
      </div>
      <div class="chart-modal-body">
        <div data-chart-host data-modal-chart="1">${lineChartHtml(series, chartOpts)}</div>
        <div data-chart-series-stats>${seriesStatsTableHtml(series, unit)}</div>
      </div>
    </div>`;
  document.body.appendChild(backdrop);
  document.body.classList.add("chart-modal-open");

  const host = backdrop.querySelector("[data-modal-chart]");
  const wrap = host?.querySelector(".chart-wrap");
  if (wrap) {
    const built = buildChartModel(series, chartOpts, chartOpts.width, chartOpts.height);
    if (built) {
      storeInteractionData(wrap, built, series, chartOpts);
      bindChartHover(wrap);
    }
  }

  openModalCtx = {
    panelId: opts.panelId || opts.id || "",
    getPanelMap: ctx.getPanelMap || (() => new Map()),
  };

  const close = () => closeChartModal();
  backdrop.querySelector(".chart-modal-close")?.addEventListener("click", close);
  backdrop.querySelector("#chartModalRefresh")?.addEventListener("click", async () => {
    // Force full soft refresh (same as menu refresh) then repaint modal from latest panel map.
    if (typeof window.__adminRefresh === "function") {
      await window.__adminRefresh();
    }
    refreshOpenChartModal();
  });
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

/** Soft-update the open enlarge modal from the latest panel map (auto-refresh / after ↻). */
export function refreshOpenChartModal() {
  if (!openModalCtx?.panelId) return;
  const backdrop = document.getElementById("chartModalBackdrop");
  if (!backdrop) return;
  const map = openModalCtx.getPanelMap?.() || new Map();
  const data = map.get(openModalCtx.panelId);
  if (!data) return;
  const host = backdrop.querySelector("[data-modal-chart]");
  if (!host) return;
  const series = data.series || [];
  const chartOpts = {
    unit: data.unit,
    width: 960,
    height: 420,
    yTicks: 8,
    interactive: true,
    range: data.range,
    tMin: data.tMin,
    tMax: data.tMax,
    step: data.step,
    lookbackMarkerSec: data.lookbackMarkerSec,
  };
  // Remount interactive chart (hover bindings need a fresh wrap).
  host.innerHTML = lineChartHtml(series, chartOpts);
  const wrap = host.querySelector(".chart-wrap");
  if (wrap) {
    const built = buildChartModel(series, chartOpts, chartOpts.width, chartOpts.height);
    if (built) {
      storeInteractionData(wrap, built, series, chartOpts);
      bindChartHover(wrap);
    }
  }
  const statsHost = backdrop.querySelector("[data-chart-series-stats]");
  if (statsHost) statsHost.innerHTML = seriesStatsTableHtml(series, data.unit);
  const title = backdrop.querySelector(".chart-modal-head h2");
  if (title && data.title) title.textContent = data.title;
}

export function closeChartModal() {
  const el = document.getElementById("chartModalBackdrop");
  if (!el) return;
  if (el._onKey) document.removeEventListener("keydown", el._onKey);
  el.remove();
  document.body.classList.remove("chart-modal-open");
  openModalCtx = null;
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
    openChartModal({ ...data, panelId }, { getPanelMap });
  });
}

function buildChartModel(series, opts, width, height) {
  const interactive = !!opts.interactive;
  const yTickCount = Math.max(2, Math.min(12, opts.yTicks || (interactive ? 8 : 3)));
  const padL = interactive ? 56 : 48;
  const padR = interactive ? 20 : 16;
  // Small cards: tight vertical padding so the plot fills the square.
  const padT = interactive ? 14 : 10;
  const padB = interactive ? 38 : 20;
  const plotW = width - padL - padR;
  const plotH = height - padT - padB;

  const allPoints = (series || []).flatMap((s) => s.points || []);
  const hasWindow = opts.tMin != null && opts.tMax != null
    && Number.isFinite(Number(opts.tMin)) && Number.isFinite(Number(opts.tMax))
    && Number(opts.tMax) > Number(opts.tMin);
  // Prefer drawing axes for the selected window even when there are zero samples
  // (e.g. no invalidations in the range — empty chart, not a "missing data" box).
  if (!allPoints.length && !hasWindow) return null;

  let dataTMin = Infinity;
  let dataTMax = -Infinity;
  let vMin = Infinity;
  let vMax = -Infinity;
  for (const p of allPoints) {
    if (p.t < dataTMin) dataTMin = p.t;
    if (p.t > dataTMax) dataTMax = p.t;
    if (p.v < vMin) vMin = p.v;
    if (p.v > vMax) vMax = p.v;
  }

  const empty = allPoints.length === 0;

  // Prefer explicit window (selected range) so X-axis shows the full time window.
  let tMin = hasWindow ? Number(opts.tMin) : dataTMin;
  let tMax = hasWindow ? Number(opts.tMax) : dataTMax;
  // Expand if samples fall slightly outside (clock skew / scrape lag).
  if (!empty && Number.isFinite(dataTMin) && dataTMin < tMin) tMin = dataTMin;
  if (!empty && Number.isFinite(dataTMax) && dataTMax > tMax) tMax = dataTMax;
  if (tMax <= tMin) tMax = tMin + 1;

  if (empty) {
    vMin = 0;
    vMax = opts.unit === "percent" ? 1 : 1;
  } else {
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
  }

  const xOf = (t) => padL + ((t - tMin) / (tMax - tMin)) * plotW;
  const yOf = (v) => padT + plotH - ((v - vMin) / (vMax - vMin)) * plotH;

  const gridYs = [];
  const axisLabels = [];
  const fractions = [];
  for (let i = 0; i < yTickCount; i++) {
    fractions.push(i / (yTickCount - 1));
  }

  const hGridSvg = fractions.map((f) => {
    const v = vMin + (vMax - vMin) * f;
    const y = yOf(v);
    gridYs.push(y);
    const label = formatAxis(v, opts.unit);
    axisLabels.push(label);
    return `<line class="chart-grid chart-grid-h" x1="${padL}" y1="${y}" x2="${width - padR}" y2="${y}" />
      <text class="chart-axis chart-axis-y" x="${padL - 6}" y="${y + 3}" text-anchor="end">${esc(label)}</text>`;
  }).join("");

  // X-axis ticks + vertical helpers across the selected time window.
  const span = tMax - tMin;
  const xTickCount = pickXTickCount(span, interactive);
  const xFracs = [];
  for (let i = 0; i < xTickCount; i++) {
    xFracs.push(i / (xTickCount - 1));
  }

  const vGridSvg = xFracs.map((f) => {
    const t = tMin + span * f;
    const x = xOf(t);
    return `<line class="chart-grid chart-grid-v" x1="${x.toFixed(1)}" y1="${padT}" x2="${x.toFixed(1)}" y2="${padT + plotH}" />`;
  }).join("");

  const xAxisSvg = xFracs.map((f, i) => {
    const t = tMin + span * f;
    const x = xOf(t);
    const anchor = i === 0 ? "start" : i === xFracs.length - 1 ? "end" : "middle";
    return `<text class="chart-axis chart-axis-x" x="${x.toFixed(1)}" y="${height - 8}" text-anchor="${anchor}">${esc(formatAxisTime(t, span))}</text>`;
  }).join("");

  const markerSec = Number(opts.lookbackMarkerSec);
  let markerSvg = "";
  if (Number.isFinite(markerSec) && markerSec > 0) {
    const tMark = tMax - markerSec;
    if (tMark > tMin && tMark < tMax) {
      const x = xOf(tMark);
      markerSvg = `<line class="chart-lookback-mark" x1="${x.toFixed(1)}" y1="0" x2="${x.toFixed(1)}" y2="${height}" pointer-events="none">
        <title>Live values use the last minute; the chart shows a longer window</title>
      </line>`;
    }
  }

  // Break the polyline when samples are farther apart than ~1.5× step (missing data),
  // instead of drawing a misleading continuous segment across the gap.
  const gapSec = resolveGapThresholdSec(opts, series);

  const paths = [];
  const pathSvg = empty
    ? ""
    : (series || []).map((s, i) => {
      const pts = s.points || [];
      if (pts.length < 1) {
        paths.push("");
        return "";
      }
      const d = buildLinePathD(pts, xOf, yOf, gapSec);
      paths.push(d);
      const color = SERIES_COLORS[i % SERIES_COLORS.length];
      const sw = interactive ? 2.1 : 1.75;
      return `<path class="chart-line" d="${d}" fill="none" stroke="${color}" stroke-width="${sw}" />`;
    }).join("");

  const legend = empty
    ? `<span class="chart-legend-item chart-no-samples" title="${esc(NO_SAMPLES_TIP)}">no samples</span>`
    : (series || []).map((s, i) => {
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
    gridSvg: hGridSvg + vGridSvg,
    markerSvg,
    xAxisSvg,
    pathSvg,
    legend,
    paths,
    gapSec,
    gridYs,
    axisLabels,
    yTickCount,
    xTickCount,
    seriesNames: empty ? [] : (series || []).map((s) => s.name || ""),
    interactive,
    empty,
  };
}

function pickXTickCount(spanSec, interactive) {
  // More ticks in modal; keep card charts readable.
  if (interactive) {
    if (spanSec <= 900) return 5;
    if (spanSec <= 3600) return 6;
    if (spanSec <= 6 * 3600) return 6;
    if (spanSec <= 24 * 3600) return 6;
    return 7;
  }
  if (spanSec <= 900) return 4;
  if (spanSec <= 3600) return 5;
  if (spanSec <= 6 * 3600) return 5;
  if (spanSec <= 24 * 3600) return 5;
  return 5;
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
  // Modal uses a Grafana-style stats table instead of the compact legend strip.
  const legend = !interactive && built.legend
    ? `<div class="chart-legend">${built.legend}</div>`
    : "";
  const emptyCls = built.empty ? " chart-wrap-empty" : "";
  return `
    <div class="chart-wrap${interactive ? " chart-wrap-interactive" : ""}${emptyCls}">
      <svg class="chart-svg" viewBox="0 0 ${built.width} ${built.height}" role="img" aria-label="Time series chart" preserveAspectRatio="xMidYMid meet">
        ${built.gridSvg}
        ${built.markerSvg || ""}
        ${built.xAxisSvg || ""}
        ${built.pathSvg}
        ${overlay}
      </svg>
      ${tip}
      ${legend}
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
  const gapSec = built.gapSec || 0;
  // Do not interpolate across a path break (same rule as buildLinePathD).
  if (gapSec > 0 && span > gapSec) {
    const nearer = (t - a.t) <= (b.t - t) ? a : b;
    return { t: nearer.t, v: nearer.v, x: xOfT(built, nearer.t), y: yOfV(built, nearer.v) };
  }
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
  const parts = [
    opts.unit || "",
    String(opts.yTicks || ""),
    opts.interactive ? "1" : "0",
    opts.range || "",
    String(opts.tMin ?? ""),
    String(opts.tMax ?? ""),
    String(opts.step || opts.stepSec || ""),
    String(opts.lookbackMarkerSec || ""),
  ];
  for (const s of series || []) {
    parts.push(s.name || "");
    for (const p of s.points || []) {
      parts.push(String(p.t), String(p.v));
    }
  }
  return parts.join("|");
}

/**
 * Parse Prometheus step tokens used by the Metrics BFF (`15s`, `1m`, `2m`, …).
 * @param {string|null|undefined} step
 * @returns {number} seconds, or 0 when unknown
 */
export function parseStepSeconds(step) {
  if (!step || typeof step !== "string") return 0;
  const d = step.trim();
  if (d.length < 2 || d.length > 8) return 0;
  const n = Number(d.slice(0, -1));
  if (!Number.isFinite(n) || n <= 0) return 0;
  const unit = d.slice(-1);
  if (unit === "s") return n;
  if (unit === "m") return n * 60;
  if (unit === "h") return n * 3600;
  if (unit === "d") return n * 86400;
  return 0;
}

/**
 * Gap threshold (seconds) above which the line path breaks instead of connecting.
 * Prefers explicit step from the API; falls back to median point spacing.
 * @param {{ step?: string|null, stepSec?: number|null }} opts
 * @param {Array<{ points?: Array<{ t: number }> }>|null|undefined} series
 */
function resolveGapThresholdSec(opts, series) {
  const fromOpts = Number(opts?.stepSec);
  if (Number.isFinite(fromOpts) && fromOpts > 0)
    return fromOpts * 1.5;
  const parsed = parseStepSeconds(opts?.step);
  if (parsed > 0)
    return parsed * 1.5;

  const deltas = [];
  for (const s of series || []) {
    const pts = s.points || [];
    for (let i = 1; i < pts.length; i++) {
      const dt = pts[i].t - pts[i - 1].t;
      if (Number.isFinite(dt) && dt > 0) deltas.push(dt);
    }
  }
  if (!deltas.length) return 120;
  deltas.sort((a, b) => a - b);
  const median = deltas[Math.floor(deltas.length / 2)];
  return Math.max(median * 1.5, 30);
}

/**
 * SVG path `d` with Move on gaps larger than gapSec.
 * @param {Array<{ t: number, v: number }>} pts
 * @param {(t: number) => number} xOf
 * @param {(v: number) => number} yOf
 * @param {number} gapSec
 */
function buildLinePathD(pts, xOf, yOf, gapSec) {
  let d = "";
  for (let i = 0; i < pts.length; i++) {
    const p = pts[i];
    const cmd = i === 0 || (gapSec > 0 && (p.t - pts[i - 1].t) > gapSec) ? "M" : "L";
    d += `${cmd}${xOf(p.t).toFixed(1)},${yOf(p.v).toFixed(1)}`;
    if (i < pts.length - 1) d += " ";
  }
  return d;
}

/**
 * Per-series window stats (Grafana-style Mean / Last / Min / Max) from sample points.
 * @param {Array<{ name?: string, points?: Array<{ t: number, v: number }> }>|null|undefined} series
 * @returns {Array<{ name: string, color: string, mean: number|null, last: number|null, min: number|null, max: number|null, count: number }>}
 */
export function computeSeriesStats(series) {
  return (series || []).map((s, i) => {
    const pts = (s.points || []).filter((p) => p && Number.isFinite(p.v));
    if (!pts.length) {
      return {
        name: s.name || `series ${i + 1}`,
        color: SERIES_COLORS[i % SERIES_COLORS.length],
        mean: null,
        last: null,
        min: null,
        max: null,
        count: 0,
      };
    }
    let min = pts[0].v;
    let max = pts[0].v;
    let sum = 0;
    let last = pts[0];
    for (const p of pts) {
      if (p.v < min) min = p.v;
      if (p.v > max) max = p.v;
      sum += p.v;
      if (p.t >= last.t) last = p;
    }
    return {
      name: s.name || `series ${i + 1}`,
      color: SERIES_COLORS[i % SERIES_COLORS.length],
      mean: sum / pts.length,
      last: last.v,
      min,
      max,
      count: pts.length,
    };
  });
}

/**
 * Grafana-style series stats table under the enlarge modal chart.
 * @param {Array} series
 * @param {string|undefined} unit
 */
export function seriesStatsTableHtml(series, unit) {
  const rows = computeSeriesStats(series);
  if (!rows.length) {
    return `<div class="chart-series-stats muted small">No series in this window.</div>`;
  }
  const body = rows.map((r) => `
    <tr>
      <td class="chart-series-name">
        <span class="chart-swatch" style="background:${r.color}"></span>
        <span title="${esc(r.name)}">${esc(r.name)}</span>
      </td>
      <td class="col-num">${esc(formatValue(r.mean, unit))}</td>
      <td class="col-num">${esc(formatValue(r.last, unit))}</td>
      <td class="col-num">${esc(formatValue(r.min, unit))}</td>
      <td class="col-num">${esc(formatValue(r.max, unit))}</td>
    </tr>`).join("");
  return `
    <div class="chart-series-stats" role="region" aria-label="Series statistics">
      <table class="chart-series-stats-table">
        <thead>
          <tr>
            <th scope="col">Name</th>
            <th scope="col" class="col-num">Mean</th>
            <th scope="col" class="col-num">Last</th>
            <th scope="col" class="col-num">Min</th>
            <th scope="col" class="col-num">Max</th>
          </tr>
        </thead>
        <tbody>${body}</tbody>
      </table>
    </div>`;
}

/** Compact X-axis label; longer windows include day. */
function formatAxisTime(t, spanSec) {
  if (t == null || Number.isNaN(t)) return "";
  const ms = t > 1e12 ? t : t * 1000;
  try {
    const d = new Date(ms);
    if (spanSec >= 86400) {
      return d.toLocaleString(undefined, { month: "short", day: "numeric", hour: "2-digit", minute: "2-digit" });
    }
    return d.toLocaleTimeString(undefined, { hour: "2-digit", minute: "2-digit" });
  } catch {
    return String(t);
  }
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
