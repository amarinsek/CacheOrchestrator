/**
 * DOM helpers for the Admin UI.
 * Keep this module dependency-free so every other module can import it safely.
 */

/** @param {string} sel CSS selector */
/** @param {ParentNode} [el=document] search root */
export const $ = (sel, el = document) => el.querySelector(sel);

/** Main content host (`#appMain`). */
export const main = () => $("#appMain");

/** Subtle chrome indicator while a soft refresh runs (no full-page loading flash). */
export function setRefreshing(on) {
  document.documentElement.classList.toggle("is-refreshing", !!on);
}

/**
 * Replace main content while preserving scroll position (used for soft refresh paints).
 * @param {string} html
 */
export function paintMain(html) {
  const el = main();
  if (!el) return;
  const y = window.scrollY;
  el.innerHTML = html;
  // Restore after layout; double-rAF covers late table layout.
  requestAnimationFrame(() => {
    window.scrollTo(0, y);
    requestAnimationFrame(() => window.scrollTo(0, y));
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
