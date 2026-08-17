/**
 * Recommendation-hint presentation and flattening.
 *
 * Live hints come from RecommendationHints on the Admin Console App.
 * This module only renders them — it does not evaluate rules.
 */

import { esc } from "./format.js";

/**
 * Severity chip stack for aggregates (header / instance / cluster).
 * Empty → neutral ○ (always show a mark so the column is never blank).
 * @param {{ critical?: number, warning?: number, info?: number, total?: number, maxSeverity?: string }|null} summary
 */
export function severityStack(summary) {
  const c = summary?.critical || 0;
  const w = summary?.warning || 0;
  const i = summary?.info || 0;
  const total = summary?.total ?? (c + w + i);
  if (!total) {
    return `<span class="sev-stack empty" title="No hints">○</span>`;
  }
  const max = summary.maxSeverity || (c ? "Critical" : w ? "Warning" : "Info");
  const parts = [];
  if (c) parts.push(`<span class="sev Critical" title="${c} critical">●${c}</span>`);
  if (w) parts.push(`<span class="sev Warning" title="${w} warning">▲${w}</span>`);
  if (i) parts.push(`<span class="sev Info" title="${i} info">i${i}</span>`);
  return `<span class="sev-stack max-${esc(max)}" title="${c} critical · ${w} warning · ${i} info">${parts.join("")}</span>`;
}

/** Compact code badges on entity list rows. Empty → ○. */
export function hintBadges(hints) {
  if (!hints || !hints.length) {
    return `<span class="hint-badges"><span class="hint empty" title="No recommendations">○</span></span>`;
  }
  return `<span class="hint-badges">${hints.map((h) =>
    `<span class="hint ${esc(h.severity || "Info")}" title="${esc(h.message)}">${esc(shortHint(h))}</span>`
  ).join("")}</span>`;
}

/** Map rule codes to short row labels. Unknown codes use severity prefix. */
export function shortHint(h) {
  const map = {
    "high-factory-share": "Factory↑",
    "critical-factory-share": "Factory‼",
    // Obsolete codes (pre-rename) still map if disabled.local or custom packs use them
    "high-origin-share": "Factory↑",
    "critical-origin-share": "Factory‼",
    "elevated-stale": "Stale",
    "frequent-invalidations": "Inv↑",
    "client-ttl-gt-output": "ClientTTL",
    "schedule-phase": "Hold",
    "schedule-approaching": "Ramp",
    "schedule-hold-lingering": "Hold!",
    "schedule-flat": "Flat",
    "factory-failures": "Factory",
    "critical-factory-failures": "Factory‼",
    "runtime-override": "Overlay",
    "fusion-hard-lt-soft": "TTL",
    "instance-oc-hit-spread": "Drift",
    "instance-factory-spread": "Drift",
    "instance-origin-spread": "Drift",
  };
  return map[h.code] || (h.severity || "Hint").slice(0, 4);
}

/** Full severity + code + message blocks (assumes non-empty). */
export function hintListHtml(hints) {
  return `<div class="hint-list">${(hints || []).map((h) => `
    <div class="hint-row ${esc(h.severity || "Info")}">
      <span class="hint-sev">${esc(h.severity || "Info")}</span>
      <div>
        <div class="hint-code"><code>${esc(h.code)}</code></div>
        <div class="hint-msg">${esc(h.message)}</div>
      </div>
    </div>`).join("")}</div>`;
}

/**
 * Detail recommendations block — omitted when empty; no heading (chips already on title).
 */
export function recommendationsSectionHtml(hints) {
  if (!hints || !hints.length) return "";
  return `<div class="recommendations-block">${hintListHtml(hints)}</div>`;
}

/**
 * Aggregate hint counts for severityStack / header badge.
 * @param {Array<{severity?: string}>|null|undefined} hints
 */
export function summarizeHints(hints) {
  let info = 0;
  let warning = 0;
  let critical = 0;
  for (const h of hints || []) {
    if (h.severity === "Critical") critical++;
    else if (h.severity === "Warning") warning++;
    else info++;
  }
  return {
    info,
    warning,
    critical,
    total: info + warning + critical,
    maxSeverity: critical ? "Critical" : warning ? "Warning" : info ? "Info" : "None",
  };
}

/**
 * Flatten domain + endpoint hints (including byInstance rows) for the Hints page.
 * Each row: { severity, code, message, instanceId, domain, route, entityType }
 *
 * @param {{ domains?: any[], endpoints?: any[] }} stats Cluster stats DTO
 */
export function collectHintRows(stats) {
  const rows = [];
  const push = (h, ctx) => {
    rows.push({
      severity: h.severity || "Info",
      code: h.code,
      message: h.message,
      instanceId: ctx.instanceId || "",
      domain: ctx.domain || "",
      route: ctx.route || "",
      entityType: ctx.entityType || "domain",
    });
  };

  // Prefer byInstance when present (avoids double count: aggregate row + each instance).
  for (const d of stats.domains || []) {
    if (d.byInstance?.length) {
      for (const bi of d.byInstance) {
        for (const h of bi.hints || []) {
          push(h, { domain: d.name, instanceId: bi.instanceId || "", entityType: "domain" });
        }
      }
    } else {
      for (const h of d.hints || []) {
        push(h, { domain: d.name, instanceId: d.instanceId || "", entityType: "domain" });
      }
    }
  }

  for (const e of stats.endpoints || []) {
    if (e.byInstance?.length) {
      for (const bi of e.byInstance) {
        for (const h of bi.hints || []) {
          push(h, {
            domain: e.configuredDomain || bi.configuredDomain || "",
            route: e.route,
            instanceId: bi.instanceId || "",
            entityType: "endpoint",
          });
        }
      }
    } else {
      for (const h of e.hints || []) {
        push(h, {
          domain: e.configuredDomain || "",
          route: e.route,
          instanceId: e.instanceId || "",
          entityType: "endpoint",
        });
      }
    }
  }

  return rows;
}
