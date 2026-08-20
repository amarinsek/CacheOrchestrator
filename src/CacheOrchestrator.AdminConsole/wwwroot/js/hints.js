/**
 * Recommendation-hint presentation and flattening.
 *
 * Live hints come from HintEngine (declarative core/operator packs) on the Admin Console App.
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

const SEV_RANK = { Critical: 0, Warning: 1, Info: 2 };

/**
 * Compact code badges on entity list rows and detail titles. Empty → ○.
 * 1–2 hints: 3-character labels (Critical, then Warning, then Info).
 * More than 2: same severity stack as the Hints nav badge.
 */
export function hintBadges(hints) {
  if (!hints || !hints.length) {
    return `<span class="hint-badges"><span class="hint empty" title="No recommendations">○</span></span>`;
  }
  const tip = hints.map((h) => `${shortHint(h)}: ${h.message || h.code || ""}`).join(" · ");
  if (hints.length > 2) {
    return `<span class="hint-badges hint-badges-compact" title="${esc(tip)}">${severityStack(summarizeHints(hints))}</span>`;
  }
  const ordered = [...hints].sort((a, b) => {
    const ra = SEV_RANK[a.severity] ?? 2;
    const rb = SEV_RANK[b.severity] ?? 2;
    if (ra !== rb) return ra - rb;
    return String(a.code || "").localeCompare(String(b.code || ""));
  });
  return `<span class="hint-badges">${ordered.map((h) =>
    `<span class="hint ${esc(h.severity || "Info")}" title="${esc(h.message)}">${esc(shortHint(h))}</span>`
  ).join("")}</span>`;
}

/**
 * Table chip: rule <c>badge</c> (max 3 runes), else ERR / WRN / INF from severity.
 */
export function shortHint(h) {
  const raw = String(h?.badge || "").trim();
  if (raw) {
    const chars = [...raw];
    return chars.length <= 3 ? raw : chars.slice(0, 3).join("");
  }
  if (h?.severity === "Critical") return "ERR";
  if (h?.severity === "Warning") return "WRN";
  return "INF";
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
 * Menu badge + domain detail use aggregate `hints`. When `byInstance` exists, older
 * logic skipped aggregate rows — domain-only rules that fire on cluster aggregates
 * then appeared in the menu/domain page but not on Hints. We keep per-instance rows
 * and still add aggregate hints that are not already present on any instance.
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

  const hintKey = (h) => `${h.severity || "Info"}|${h.code || ""}|${h.message || ""}`;

  for (const d of stats.domains || []) {
    const fromInstances = new Set();
    for (const bi of d.byInstance || []) {
      for (const h of bi.hints || []) {
        fromInstances.add(hintKey(h));
        push(h, { domain: d.name, instanceId: bi.instanceId || "", entityType: "domain" });
      }
    }
    for (const h of d.hints || []) {
      // Skip aggregate duplicate when the same hint already appears per-instance.
      if (fromInstances.has(hintKey(h))) continue;
      push(h, { domain: d.name, instanceId: d.instanceId || "", entityType: "domain" });
    }
  }

  for (const e of stats.endpoints || []) {
    const fromInstances = new Set();
    for (const bi of e.byInstance || []) {
      for (const h of bi.hints || []) {
        fromInstances.add(hintKey(h));
        push(h, {
          domain: e.configuredDomain || bi.configuredDomain || "",
          route: e.route,
          instanceId: bi.instanceId || "",
          entityType: "endpoint",
        });
      }
    }
    for (const h of e.hints || []) {
      if (fromInstances.has(hintKey(h))) continue;
      push(h, {
        domain: e.configuredDomain || "",
        route: e.route,
        instanceId: e.instanceId || "",
        entityType: "endpoint",
      });
    }
  }

  return rows;
}
