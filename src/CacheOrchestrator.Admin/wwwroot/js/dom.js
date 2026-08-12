/**
 * DOM helpers for the Admin UI.
 * Keep this module dependency-free so every other module can import it safely.
 */

/** @param {string} sel CSS selector */
/** @param {ParentNode} [el=document] search root */
export const $ = (sel, el = document) => el.querySelector(sel);

/** Main content host (`#appMain`). */
export const main = () => $("#appMain");
