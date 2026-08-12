/**
 * Hash-based SPA router helpers.
 * Routes look like `#/endpoints?sort=requests&search=hello`.
 */

import { $, main } from "./dom.js";
import { esc } from "./format.js";

/** Parse `location.hash` into path + URLSearchParams. */
export function parseHash() {
  const raw = (location.hash || "#/overview").replace(/^#\/?/, "");
  const [pathPart, queryPart] = raw.split("?");
  const path = (pathPart || "overview").replace(/\/$/, "") || "overview";
  const params = new URLSearchParams(queryPart || "");
  return { path, params };
}

/**
 * Navigate by setting location.hash (triggers hashchange → route()).
 * Empty/null params are omitted from the query string.
 */
export function navigate(path, params = {}) {
  const q = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    if (v != null && v !== "") q.set(k, v);
  }
  const qs = q.toString();
  location.hash = "#/" + path + (qs ? "?" + qs : "");
}

/** Highlight the active top-nav item from the first path segment. */
export function setNavActive(path) {
  const root = path.split("/")[0] || "overview";
  document.querySelectorAll(".app-nav a").forEach((a) => {
    a.classList.toggle("active", a.dataset.nav === root);
  });
}

/** Breadcrumb under chrome: [{ label, href? }, ...]. */
export function setBreadcrumb(parts) {
  const el = $("#breadcrumb");
  if (!parts || !parts.length) {
    el.innerHTML = "";
    return;
  }
  el.innerHTML = parts.map((p, i) => {
    if (p.href && i < parts.length - 1)
      return `<a href="${esc(p.href)}">${esc(p.label)}</a>`;
    return `<span>${esc(p.label)}</span>`;
  }).join(" <span class='muted'>/</span> ");
}

export { main, $ };
