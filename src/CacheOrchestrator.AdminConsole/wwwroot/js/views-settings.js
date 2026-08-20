/**
 * Settings page — hint rules catalog.
 */

import { api } from "./api.js";
import { $, beginPageLoad, main, paintPage } from "./dom.js";
import { esc, num } from "./format.js";
import { setBreadcrumb, setNavActive } from "./router.js";
import { bindEmptyStateActions, emptyStateHtml } from "./tables.js";

export async function renderSettingsPage() {
  setNavActive("settings");
  setBreadcrumb([]);
  beginPageLoad(false, `<div class="card"><p class="muted">Loading settings…</p></div>`);

  let payload;
  try {
    payload = await api("/api/hints/rules");
  } catch (err) {
    paintPage(`<div class="card">${emptyStateHtml("error", { detail: err.message })}</div>`, false);
    bindEmptyStateActions(main());
    return;
  }

  const load = payload.load || {};
  const rules = payload.rules || [];
  /** @type {Map<string, any>} id -> rule */
  const ruleById = new Map((rules || []).map((r) => [r.id, r]));
  const issues = load.errors || [];
  const hard = issues.filter((e) => e.level !== "warning");
  const warns = issues.filter((e) => e.level === "warning");
  const loadBadge = hard.length
    ? `<span class="badge bad">ERROR</span>`
    : warns.length
      ? `<span class="badge warn">WARN</span>`
      : `<span class="badge ok">OK</span>`;

  const issueTable = (list) => `
        <table class="dense hint-compile-error-table">
          <thead><tr><th>File</th><th>Rule</th><th>Path</th><th>Message</th></tr></thead>
          <tbody>
            ${list.map((e) => `
              <tr>
                <td><code>${esc(e.file || "")}</code></td>
                <td>${e.ruleCode ? `<code>${esc(e.ruleCode)}</code>` : `<span class="muted">—</span>`}</td>
                <td><code>${esc(e.path || "")}</code></td>
                <td>${esc(e.message || "")}</td>
              </tr>`).join("")}
          </tbody>
        </table>`;

  const errBlock = hard.length
    ? `<div class="card hint-compile-error-card">
        <div class="card-head">
          <h2><span class="badge bad">ERROR</span> Rule compile issues
            <span class="badge bad">${hard.length}</span></h2>
        </div>
        <p class="hint-compile-error-lead">
          ${hard.length} problem(s) in rule files — rules with errors were not loaded.
          Fix JSON, then <strong>Reload</strong>. <strong>Rule</strong> names the entry; <strong>Path</strong> is inside that rule.
        </p>
        ${issueTable(hard)}
      </div>`
    : "";
  const warnBlock = warns.length
    ? `<div class="card">
        <div class="card-head">
          <h2><span class="badge warn">WARN</span> Rule compile warnings
            <span class="badge warn">${warns.length}</span></h2>
        </div>
        <p class="muted" style="margin:0 0 0.75rem">
          Rules still loaded. Badge longer than 3 characters or a duplicate <code>badge</code> among different codes.
        </p>
        ${issueTable(warns)}
      </div>`
    : "";

  const groups = groupHintRulesBySource(rules);
  const groupHtml = groups.map(([source, list], gi) => {
    const open = gi === 0; // core pack open by default
    const label = sourceLabel(source);
    const coreBadge = list.some((r) => r.isBuiltIn) ? ` <span class="badge muted">core</span>` : "";
    return `
      <div class="hint-rule-group" data-source="${esc(source)}">
        <button type="button" class="hint-group-toggle" aria-expanded="${open ? "true" : "false"}">
          <span class="hint-group-chevron">${open ? "▼" : "▶"}</span>
          <span>${esc(label)}${coreBadge}</span>
          <span class="badge">${list.length}</span>
        </button>
        <div class="hint-group-body${open ? "" : " hidden"}">
          <div class="table-wrap">
            <table class="dense entity-table">
              <thead>
                <tr>
                  <th>Enabled</th><th>Code</th><th>Badge</th><th>Severity</th><th>Scope</th>
                  <th>Category</th><th>Description</th>
                </tr>
              </thead>
              <tbody>
                ${list.map((r) => `
                  <tr class="hint-rule-row clickable" data-rule-id="${esc(r.id)}" title="Click to view rule definition">
                    <td>
                      <input type="checkbox" class="hint-rule-enabled" data-code="${esc(r.code)}"
                        ${r.enabled ? "checked" : ""} title="Enable / disable ${esc(r.code)}" />
                    </td>
                    <td><code class="hint-rule-code-link">${esc(r.code)}</code></td>
                    <td>${r.badge ? `<code>${esc(r.badge)}</code>` : `<span class="muted">—</span>`}</td>
                    <td>${severityCell(r.defaultSeverity)}</td>
                    <td>${esc(r.scope || "")}</td>
                    <td>${esc(r.category || "—")}</td>
                    <td class="muted">${esc(r.description || "")}</td>
                  </tr>`).join("")}
              </tbody>
            </table>
          </div>
        </div>
      </div>`;
  }).join("");

  paintPage(`
    <div class="card">
      <div class="card-head">
        <h2>Hint rules ${loadBadge}</h2>
        <div class="chart-card-actions">
          <button type="button" class="secondary" id="btnHintsReload">Reload files</button>
        </div>
      </div>
      <p class="muted">
        Product defaults: <code>hints/core-hints.json</code> (always loaded).
        Extra packs: <code>AdminConsole:Hints:RuleFiles</code>
        (Development: <code>hints/*.json</code>; Production/Docker: <code>data/rules/*.json</code>).
        Disable codes here (saved to <code>DisabledStatePath</code>, e.g. <code>hints/disabled.local.json</code>
        or <code>data/disabled.local.json</code>) or via <code>DisabledCodes</code> in config.
        See <code>hints/README.md</code>.
      </p>
      <p class="muted small">
        Loaded ${num(load.ruleCount)} rule(s) from ${num(load.fileCount)} file(s)
        · ${load.loadedAtUtc ? esc(String(load.loadedAtUtc)) : "—"}
        · Click a rule row to view its JSON definition.
      </p>
    </div>
    ${errBlock}
    ${warnBlock}
    <div class="card">
      <h2>Catalog <span class="badge">${rules.length}</span></h2>
      <p class="muted small">Groups are one per rule file. Click the header to collapse / expand.</p>
      <div id="hintRulesCatalog">${groupHtml || `<p class="muted">No rules loaded.</p>`}</div>
    </div>
    <div class="card">
      <h2>Known paths <span class="badge">${(payload.knownPaths || []).length}</span></h2>
      <p class="muted">Use these <code>path</code> values in declarative <code>when</code> conditions.</p>
      <p class="muted small" style="max-height:10rem;overflow:auto">
        ${(payload.knownPaths || []).map((p) => `<code>${esc(p)}</code>`).join(" · ")}
      </p>
    </div>`, false);

  main().querySelectorAll(".hint-group-toggle").forEach((btn) => {
    btn.addEventListener("click", () => {
      const body = btn.parentElement?.querySelector(".hint-group-body");
      const chev = btn.querySelector(".hint-group-chevron");
      if (!body) return;
      const open = body.classList.toggle("hidden");
      // classList.toggle returns true if class is now present; hidden => collapsed
      const expanded = !open;
      btn.setAttribute("aria-expanded", expanded ? "true" : "false");
      if (chev) chev.textContent = expanded ? "▼" : "▶";
    });
  });

  $("#btnHintsReload")?.addEventListener("click", async () => {
    try {
      await api("/api/hints/reload", { method: "POST", body: "{}" });
      await renderSettingsPage();
    } catch (err) {
      alert("Reload failed: " + err.message);
    }
  });

  main().querySelectorAll(".hint-rule-enabled").forEach((cb) => {
    cb.addEventListener("click", (ev) => ev.stopPropagation());
    cb.addEventListener("change", async () => {
      const code = cb.getAttribute("data-code");
      if (!code) return;
      cb.disabled = true;
      try {
        await api(`/api/hints/rules/${encodeURIComponent(code)}/enabled`, {
          method: "PUT",
          body: JSON.stringify({ enabled: !!cb.checked }),
        });
        // Same code may appear in domain + endpoint rows — keep checkboxes in sync.
        main().querySelectorAll(".hint-rule-enabled").forEach((el) => {
          if (el !== cb && el.getAttribute("data-code") === code) el.checked = cb.checked;
        });
      } catch (err) {
        cb.checked = !cb.checked;
        alert("Could not update rule: " + err.message);
      } finally {
        cb.disabled = false;
      }
    });
  });

  main().querySelectorAll("tr.hint-rule-row").forEach((tr) => {
    tr.addEventListener("click", (ev) => {
      if (ev.target?.closest?.("input, button, a, label")) return;
      const id = tr.getAttribute("data-rule-id");
      const rule = id ? ruleById.get(id) : null;
      if (rule) openHintRuleDetail(rule);
    });
  });
}

export function severityCell(sev) {
  if (!sev) return `<span class="muted">—</span>`;
  const s = String(sev);
  const cls = s === "Critical" ? "sev-Critical" : s === "Warning" ? "sev-Warning" : "sev-Info";
  return `<span class="hint-sev-label ${cls}">${esc(s)}</span>`;
}

/* Standard “two sheets” copy icon (stroke, Lucide-style). */
const COPY_ICON_SVG = `<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><rect x="9" y="9" width="13" height="13" rx="2" ry="2"/><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/></svg>`;
const CLOSE_ICON_SVG = `<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><path d="M18 6 6 18"/><path d="m6 6 12 12"/></svg>`;

/** Modal with pretty-printed rule JSON. */
export function openHintRuleDetail(rule) {
  closeHintRuleDetail();
  const title = `${rule.code || "rule"} · ${rule.scope || ""}`;
  let body = rule.definitionJson;
  if (!body) {
    body = JSON.stringify({
      code: rule.code,
      severity: rule.defaultSeverity,
      category: rule.category,
      scope: rule.scope,
      description: rule.description,
      source: rule.source,
      note: "Definition JSON not available for this rule.",
    }, null, 2);
  }
  const backdrop = document.createElement("div");
  backdrop.className = "chart-modal-backdrop";
  backdrop.id = "hintRuleDetailBackdrop";
  backdrop.innerHTML = `
    <div class="chart-modal hint-rule-detail-modal" role="dialog" aria-modal="true" aria-label="${esc(title)}">
      <div class="chart-modal-head">
        <h2>
          <code>${esc(rule.code || "")}</code>
          ${severityCell(rule.defaultSeverity)}
          <span class="badge muted">${esc(rule.scope || "")}</span>
        </h2>
        <div class="chart-modal-actions">
          <button type="button" class="secondary chart-modal-icon-btn hint-rule-copy" aria-label="Copy rule JSON" title="Copy rule JSON">${COPY_ICON_SVG}</button>
          <button type="button" class="secondary chart-modal-icon-btn chart-modal-close" aria-label="Close" title="Close">${CLOSE_ICON_SVG}</button>
        </div>
      </div>
      <p class="muted small" style="margin:0 0 0.5rem">
        ${esc(sourceLabel(rule.source || ""))}
        ${rule.category ? ` · ${esc(rule.category)}` : ""}
        ${rule.enabled === false ? ` · <span class="status-Down">disabled</span>` : ""}
      </p>
      <pre class="hint-rule-json result">${esc(body)}</pre>
    </div>`;
  document.body.appendChild(backdrop);
  document.body.classList.add("chart-modal-open");
  const close = () => closeHintRuleDetail();
  backdrop.querySelector(".chart-modal-close")?.addEventListener("click", close);
  const copyBtn = backdrop.querySelector(".hint-rule-copy");
  copyBtn?.addEventListener("click", async () => {
    const ok = await copyTextToClipboard(body);
    if (!copyBtn) return;
    copyBtn.classList.toggle("copied", ok);
    copyBtn.title = ok ? "Copied" : "Copy failed";
    copyBtn.setAttribute("aria-label", ok ? "Copied" : "Copy failed");
    window.setTimeout(() => {
      if (!copyBtn.isConnected) return;
      copyBtn.classList.remove("copied");
      copyBtn.title = "Copy rule JSON";
      copyBtn.setAttribute("aria-label", "Copy rule JSON");
    }, 1500);
  });
  backdrop.addEventListener("click", (ev) => {
    if (ev.target === backdrop) close();
  });
  const onKey = (ev) => {
    if (ev.key === "Escape") close();
  };
  backdrop._onKey = onKey;
  document.addEventListener("keydown", onKey);
}

/** @param {string} text */
export async function copyTextToClipboard(text) {
  try {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(text);
      return true;
    }
  } catch {
    /* fall through */
  }
  try {
    const ta = document.createElement("textarea");
    ta.value = text;
    ta.setAttribute("readonly", "");
    ta.style.position = "fixed";
    ta.style.left = "-9999px";
    document.body.appendChild(ta);
    ta.select();
    const ok = document.execCommand("copy");
    ta.remove();
    return ok;
  } catch {
    return false;
  }
}

export function closeHintRuleDetail() {
  const el = document.getElementById("hintRuleDetailBackdrop");
  if (!el) return;
  if (el._onKey) document.removeEventListener("keydown", el._onKey);
  el.remove();
  document.body.classList.remove("chart-modal-open");
}

/** @param {Array} rules */
export function groupHintRulesBySource(rules) {
  /** @type {Map<string, any[]>} */
  const map = new Map();
  for (const r of rules || []) {
    const key = r.source || "unknown";
    if (!map.has(key)) map.set(key, []);
    map.get(key).push(r);
  }
  // core file first
  return [...map.entries()].sort((a, b) => {
    const ac = /core-hints/i.test(a[0]) ? 0 : 1;
    const bc = /core-hints/i.test(b[0]) ? 0 : 1;
    if (ac !== bc) return ac - bc;
    return a[0].localeCompare(b[0]);
  });
}

export function sourceLabel(source) {
  const s = String(source || "");
  if (s.startsWith("file:")) return s.slice(5);
  return s;
}
