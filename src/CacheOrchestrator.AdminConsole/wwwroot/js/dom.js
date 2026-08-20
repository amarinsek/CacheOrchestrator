/**
 * DOM helpers for the Admin UI.
 * Keep this module dependency-free so every other module can import it safely.
 */

/** @param {string} sel CSS selector */
/** @param {ParentNode} [el=document] search root */
export const $ = (sel, el = document) => el.querySelector(sel);

/** Main content host (`#appMain`). */
export const main = () => $("#appMain");

/** Keep Reload spin + "Loading" visible at least this long (fast fetches otherwise just flicker). */
const MIN_REFRESH_VISIBLE_MS = 1000;

let refreshShownAt = 0;
let refreshHideTimer = null;

function applyRefreshingUi(busy) {
  document.documentElement.classList.toggle("is-refreshing", busy);
  const btn = document.getElementById("btnHeaderRefresh");
  if (btn) {
    btn.setAttribute("aria-busy", busy ? "true" : "false");
    btn.classList.toggle("is-loading", busy);
  }
  const label = document.querySelector("[data-refresh-label]");
  if (label) label.textContent = busy ? "Loading" : "Reload";
}

/** Subtle chrome indicator while a soft refresh runs (no full-page loading flash). */
export function setRefreshing(on) {
  if (on) {
    if (refreshHideTimer) {
      clearTimeout(refreshHideTimer);
      refreshHideTimer = null;
    }
    if (!refreshShownAt) refreshShownAt = Date.now();
    applyRefreshingUi(true);
    return;
  }

  const elapsed = refreshShownAt ? Date.now() - refreshShownAt : MIN_REFRESH_VISIBLE_MS;
  const wait = Math.max(0, MIN_REFRESH_VISIBLE_MS - elapsed);
  const hide = () => {
    refreshHideTimer = null;
    refreshShownAt = 0;
    applyRefreshingUi(false);
  };
  if (wait === 0) {
    hide();
    return;
  }
  if (refreshHideTimer) return;
  refreshHideTimer = setTimeout(hide, wait);
}

/** Page scroll host under the chrome (`#appScroll`), or window as fallback. */
export function scrollRoot() {
  return document.getElementById("appScroll") || document.scrollingElement || document.documentElement;
}

/**
 * Replace main content while preserving scroll position (used for soft refresh paints).
 * @param {string} html
 */
export function paintMain(html) {
  const el = main();
  if (!el) return;
  const root = scrollRoot();
  const y = root.scrollTop ?? window.scrollY ?? 0;
  el.innerHTML = html;
  // Restore after layout; double-rAF covers late table layout.
  const restore = () => {
    if ("scrollTop" in root) root.scrollTop = y;
    else window.scrollTo(0, y);
  };
  requestAnimationFrame(() => {
    restore();
    requestAnimationFrame(restore);
  });
}

/** True when main already has a painted page (not first blank/loading state). */
export function mainHasContent() {
  const el = main();
  if (!el) return false;
  const t = (el.textContent || "").trim();
  if (!t) return false;
  // Any intermediate loading placeholder is not "real" content.
  if (/^Loading\b/i.test(t)) return false;
  if (el.querySelector?.(".kpi-row, .entity-table, .chart-card, .toolbar, .dist-banner"))
    return true;
  return el.children.length > 0 && !/loading/i.test(t);
}

/**
 * First paint may show loading; soft refresh keeps previous content until data arrives.
 * @param {boolean} soft
 * @param {string} loadingHtml
 */
export function beginPageLoad(soft, loadingHtml) {
  if (!soft || !mainHasContent()) {
    main().innerHTML = loadingHtml;
  }
}

/**
 * Soft refresh preserves scroll; hard navigation replaces immediately.
 * @param {string} html
 * @param {boolean} soft
 */
export function paintPage(html, soft) {
  if (soft) paintMain(html);
  else main().innerHTML = html;
}

/**
 * Shared KPI strip markup.
 * @param {Array<{ label: string, valueHtml: string, title?: string, tipAttr?: string, className?: string, valueClass?: string, attrs?: string }>} items
 * @param {string} [id] optional element id on the row
 */
export function kpiRowHtml(items, id) {
  const idAttr = id ? ` id="${id}"` : "";
  const cells = (items || []).map((it) => {
    const title = it.title ? ` title="${it.title}"` : "";
    const tip = it.tipAttr || "";
    const cls = it.className ? ` ${it.className}` : "";
    const valueCls = it.valueClass ? ` ${it.valueClass}` : "";
    const extra = it.attrs ? ` ${it.attrs}` : "";
    return `<div class="kpi${cls}"${title}${tip}${extra}><div class="label">${it.label}</div><div class="value${valueCls}">${it.valueHtml}</div></div>`;
  }).join("");
  return `<div class="kpi-row"${idAttr}>${cells}</div>`;
}
