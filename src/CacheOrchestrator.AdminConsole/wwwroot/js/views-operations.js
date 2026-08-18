/**
 * Operations page — invalidate / version / settings fan-out.
 */

import { api } from "./api.js";
import { $, main } from "./dom.js";
import { esc } from "./format.js";
import { setBreadcrumb } from "./router.js";
import * as shell from "./shell.js";
import { shortError } from "./views-shared.js";

/** Normalize legacy action=ttl → settings. */
function normalizeAction(raw) {
  const a = String(raw || "invalidate").trim().toLowerCase();
  if (a === "ttl" || a === "patch-ttl" || a === "settings") return "settings";
  return a;
}

/**
 * @param {Array<{ id: string, displayName?: string, group?: string, kind?: string, runtimeOverlay?: boolean, enumValues?: string[] }>} catalog
 */
function overlayCatalog(catalog) {
  return (catalog || []).filter((s) => s.runtimeOverlay);
}

export async function renderOperations(params) {
  setBreadcrumb([]);
  const domainParam = params.get("domain") || "";
  const target = params.get("target") || "all";
  const action = normalizeAction(params.get("action") || "invalidate");

  const [instances, distribution, domainsFan, catalogDto] = await Promise.all([
    api("/api/instances"),
    api("/api/distribution").catch(() => null),
    api("/api/domains").catch(() => ({ data: [] })),
    api("/api/domain-settings/catalog").catch(() => ({ settings: [] })),
  ]);

  const domains = (domainsFan?.data || [])
    .map((d) => d.name)
    .filter(Boolean)
    .sort((a, b) => a.localeCompare(b));
  if (domainParam && !domains.includes(domainParam)) domains.unshift(domainParam);
  const selectedDomain = domainParam || domains[0] || "";

  const catalog = overlayCatalog(catalogDto?.settings || []);

  const mode = distribution?.recommendedMode || "fan-out";
  const busAvailable = !!distribution?.busAvailable;
  const modeClass = mode === "bus-distribute" ? "mode-bus" : "mode-fanout";
  const modeLabel = mode === "bus-distribute" ? "Cluster bus" : "Direct to each instance";
  const modeDetail = distribution?.summary
    || "How this Console will deliver the operation.";

  const probeRows = (distribution?.instances || []).map((p) => {
    const bus = p.busEnabled
      ? `<span class="badge ok">bus</span>`
      : `<span class="badge muted">no bus</span>`;
    const mem = p.membership ? esc(p.membership) : "—";
    const peers = p.peerCount != null ? p.peerCount : "—";
    const st = p.succeeded ? "ok" : "bad";
    return `<tr>
      <td>${esc(p.id)}</td>
      <td class="${st}">${p.succeeded ? "reachable" : "down"}</td>
      <td>${bus}</td>
      <td>${mem}</td>
      <td>${peers}</td>
      <td class="muted" title="${p.error ? esc(p.error) : ""}">${p.error ? esc(shortError(p.error)) : ""}</td>
    </tr>`;
  }).join("");

  main().innerHTML = `
    <div class="card">
      <h2>Cluster bus probe</h2>
        <div id="distBanner" class="dist-banner ${modeClass}">
            <div class="dist-banner-title">
                <span class="badge ${mode === "bus-distribute" ? "ok" : "warn"}">${esc(modeLabel)}</span>
                ${busAvailable && distribution?.preferredBusOriginId
                ? `<span class="muted">preferred origin: <code>${esc(distribution.preferredBusOriginId)}</code></span>`
                : ""}
            </div>
            <p class="muted dist-banner-detail">${esc(modeDetail)}</p>
            <p class="muted small">
                <strong>Direct</strong> — this Console calls each selected instance.
                <strong>Cluster bus</strong> — one origin receives the command; peers apply it via the bus.
            </p>
        </div>
      <div class="table-wrap">
        <table class="data">
          <thead>
            <tr><th>Instance</th><th>Probe</th><th>Bus</th><th>Membership</th><th>Peers</th><th>Error</th></tr>
          </thead>
          <tbody>
            ${probeRows || `<tr><td colspan="6" class="muted">No instances configured.</td></tr>`}
          </tbody>
        </table>
      </div>
    </div>
    <div class="card">
      <h2>Operations</h2>
      <form id="opForm" class="op-form">
        <div class="op-field">
          <label for="opAction">Action</label>
          <select id="opAction" name="action">
            <option value="invalidate" ${action === "invalidate" ? "selected" : ""}>Invalidate domain</option>
            <option value="entity" ${action === "entity" ? "selected" : ""}>Invalidate entity</option>
            <option value="version" ${action === "version" ? "selected" : ""}>Bump version</option>
            <option value="settings" ${action === "settings" ? "selected" : ""}>Patch settings</option>
          </select>
        </div>
        <div class="op-field">
          <label for="opDomain">Domain</label>
          <select id="opDomain" name="domain" required ${domains.length ? "" : "disabled"}>
            ${domains.length
              ? domains.map((d) =>
                  `<option value="${esc(d)}" ${d === selectedDomain ? "selected" : ""}>${esc(d)}</option>`).join("")
              : `<option value="">No domains available</option>`}
          </select>
        </div>
        <div class="op-field">
          <label for="opTarget">Target</label>
          <select id="opTarget" name="target">
            <option value="all" ${target === "all" ? "selected" : ""}>all</option>
            ${(instances || []).map((i) =>
              `<option value="instance:${esc(i.id)}" ${target === `instance:${i.id}` ? "selected" : ""}>instance:${esc(i.id)}</option>`
            ).join("")}
          </select>
        </div>
        <div id="opActionPanel" class="op-panel"></div>
        <div class="op-actions">
          <button type="submit">Run</button>
        </div>
      </form>
      <div id="opModeUsed" class="dist-result-meta muted">No operation yet.</div>
      <pre id="opResult" class="result">No operation yet.</pre>
    </div>`;

  const actionEl = $("#opAction");
  const panel = $("#opActionPanel");
  /** @type {SettingRowsController|null} */
  let settingsCtrl = null;

  function mountActionPanel() {
    const a = normalizeAction(actionEl.value);
    settingsCtrl = null;
    if (a === "entity") {
      panel.innerHTML = `
        <div class="op-field">
          <label for="opEntityKind">Entity kind</label>
          <input id="opEntityKind" type="text" placeholder="products" autocomplete="off" />
        </div>
        <div class="op-field">
          <label for="opEntity">Entity id</label>
          <input id="opEntity" type="text" placeholder="resource id" autocomplete="off" />
        </div>`;
      return;
    }
    if (a === "version") {
      panel.innerHTML = `
        <div class="op-field">
          <label for="opVersion">Version (optional)</label>
          <input id="opVersion" type="text" placeholder="auto if empty" autocomplete="off" />
        </div>`;
      return;
    }
    if (a === "settings") {
      panel.innerHTML = `
        <p class="muted small op-panel-hint">Choose one or more runtime settings, then Run. Only overlay-capable keys are listed.</p>
        <div id="opSettingRows" class="op-setting-rows"></div>`;
      settingsCtrl = new SettingRowsController($("#opSettingRows"), catalog);
      settingsCtrl.render();
      return;
    }
    panel.innerHTML = `<p class="muted small op-panel-hint">Invalidates the selected domain on the target instance(s).</p>`;
  }

  actionEl.addEventListener("change", mountActionPanel);
  mountActionPanel();

  function renderModeUsed(result) {
    const meta = $("#opModeUsed");
    if (!result) {
      meta.textContent = "No operation yet.";
      return;
    }
    const m = result.distributionMode || "fan-out";
    const badge = m === "bus-distribute"
      ? `<span class="badge ok">cluster bus</span>`
      : `<span class="badge warn">direct</span>`;
    const origin = result.busOriginInstanceId
      ? ` · origin <code>${esc(result.busOriginInstanceId)}</code>`
      : "";
    const dist = result.distribute ? "distribute:true" : "distribute:false";
    meta.innerHTML = `${badge} · ${dist}${origin}<br/><span class="muted">${esc(result.distributionSummary || "")}</span>`;
  }

  $("#opForm").addEventListener("submit", async (ev) => {
    ev.preventDefault();
    const a = normalizeAction(actionEl.value);
    const dom = $("#opDomain").value.trim();
    const tgt = $("#opTarget").value;
    const out = $("#opResult");
    out.textContent = "Running…";
    $("#opModeUsed").textContent = "Running…";

    if (!dom) {
      $("#opModeUsed").textContent = "Error";
      out.textContent = "Error: Domain is required.";
      return;
    }

    try {
      let result;
      if (a === "invalidate") {
        result = await api("/api/invalidate", {
          method: "POST",
          body: JSON.stringify({ scope: "domain", domain: dom, target: tgt }),
        });
      } else if (a === "entity") {
        result = await api("/api/invalidate", {
          method: "POST",
          body: JSON.stringify({
            scope: "entity",
            domain: dom,
            entityKind: $("#opEntityKind").value.trim(),
            entityId: $("#opEntity").value.trim(),
            target: tgt,
          }),
        });
      } else if (a === "version") {
        const version = $("#opVersion").value.trim();
        result = await api(`/api/domains/${encodeURIComponent(dom)}/version`, {
          method: "POST",
          body: JSON.stringify({ version: version || null, target: tgt }),
        });
      } else {
        if (!settingsCtrl) throw new Error("Settings panel is not ready.");
        const built = settingsCtrl.buildSettings();
        if (built.error) {
          $("#opModeUsed").textContent = "Error";
          out.textContent = "Error: " + built.error;
          return;
        }
        result = await api(`/api/domains/${encodeURIComponent(dom)}/settings`, {
          method: "PATCH",
          body: JSON.stringify({ settings: built.settings, target: tgt }),
        });
      }
      renderModeUsed(result);
      out.textContent = JSON.stringify(result, null, 2);
      shell.refreshHeader();
    } catch (err) {
      $("#opModeUsed").textContent = "Error";
      out.textContent = "Error: " + err.message;
    }
  });
}

/**
 * Multi-row setting editor driven by domain-settings catalog (overlay entries).
 */
class SettingRowsController {
  /**
   * @param {HTMLElement} host
   * @param {Array<{ id: string, displayName?: string, group?: string, kind?: string, enumValues?: string[] }>} catalog
   */
  constructor(host, catalog) {
    this.host = host;
    /** @type {Array<{ key: string, value: string }>} */
    this.rows = [{ key: "", value: "" }];
    this.catalog = catalog || [];
  }

  render() {
    if (!this.catalog.length) {
      this.host.innerHTML = `<p class="muted">No overlay settings in catalog (is Admin API reachable?).</p>`;
      return;
    }
    const used = new Set(this.rows.map((r) => r.key).filter(Boolean));
    this.host.innerHTML = this.rows.map((row, idx) => {
      const isLast = idx === this.rows.length - 1;
      const options = this.catalog
        .filter((s) => s.id === row.key || !used.has(s.id))
        .map((s) => {
          const label = s.group ? `${s.group}: ${s.displayName || s.id}` : (s.displayName || s.id);
          return `<option value="${esc(s.id)}" ${s.id === row.key ? "selected" : ""}>${esc(label)}</option>`;
        })
        .join("");
      const entry = this.catalog.find((s) => s.id === row.key);
      const valueControl = row.key
        ? this.valueControlHtml(entry, row.value, idx)
        : "";
      const removeBtn = !isLast || row.key
        ? `<button type="button" class="secondary op-setting-remove" data-idx="${idx}" title="Remove row" aria-label="Remove">×</button>`
        : `<span class="op-setting-remove-spacer"></span>`;
      return `
        <div class="op-setting-row" data-idx="${idx}">
          <select class="op-setting-key" data-idx="${idx}" aria-label="Setting">
            <option value="">Select setting…</option>
            ${options}
          </select>
          <div class="op-setting-value-wrap">${valueControl}</div>
          ${removeBtn}
        </div>`;
    }).join("");

    this.host.querySelectorAll(".op-setting-key").forEach((sel) => {
      sel.addEventListener("change", (ev) => {
        const i = Number(ev.target.dataset.idx);
        const key = ev.target.value;
        this.rows[i] = { key, value: "" };
        const last = this.rows[this.rows.length - 1];
        if (last.key) this.rows.push({ key: "", value: "" });
        this.render();
      });
    });
    this.host.querySelectorAll(".op-setting-value").forEach((inp) => {
      inp.addEventListener("input", (ev) => {
        const i = Number(ev.target.dataset.idx);
        this.rows[i].value = ev.target.type === "checkbox"
          ? (ev.target.checked ? "true" : "false")
          : ev.target.value;
      });
      inp.addEventListener("change", (ev) => {
        const i = Number(ev.target.dataset.idx);
        if (ev.target.type === "checkbox") {
          this.rows[i].value = ev.target.checked ? "true" : "false";
        }
      });
    });
    this.host.querySelectorAll(".op-setting-remove").forEach((btn) => {
      btn.addEventListener("click", () => {
        const i = Number(btn.dataset.idx);
        this.rows.splice(i, 1);
        if (!this.rows.length || this.rows[this.rows.length - 1].key) {
          this.rows.push({ key: "", value: "" });
        }
        this.render();
      });
    });
  }

  /**
   * @param {{ kind?: string, enumValues?: string[] }|undefined} entry
   * @param {string} value
   * @param {number} idx
   */
  valueControlHtml(entry, value, idx) {
    const kind = normalizeKind(entry?.kind);
    if (kind === "bool") {
      const checked = value === "true" || value === "1";
      return `<label class="op-setting-bool"><input type="checkbox" class="op-setting-value" data-idx="${idx}" ${checked ? "checked" : ""} /> enabled</label>`;
    }
    if (kind === "enum" && entry?.enumValues?.length) {
      const opts = entry.enumValues.map((v) =>
        `<option value="${esc(v)}" ${v === value ? "selected" : ""}>${esc(v)}</option>`).join("");
      return `<select class="op-setting-value" data-idx="${idx}" aria-label="Value"><option value="">Select…</option>${opts}</select>`;
    }
    if (kind === "datetimeoffset") {
      // datetime-local wants local wall time; store ISO on submit
      return `<input type="datetime-local" class="op-setting-value" data-idx="${idx}" value="${esc(value)}" aria-label="Value" />`;
    }
    if (kind === "double") {
      return `<input type="number" step="any" class="op-setting-value" data-idx="${idx}" value="${esc(value)}" aria-label="Value" />`;
    }
    // Int / IntSeconds default
    return `<input type="number" min="0" step="1" class="op-setting-value" data-idx="${idx}" value="${esc(value)}" aria-label="Value" />`;
  }

  /** @returns {{ settings?: Record<string, unknown>, error?: string }} */
  buildSettings() {
    /** @type {Record<string, unknown>} */
    const settings = {};
    for (const row of this.rows) {
      if (!row.key) continue;
      const entry = this.catalog.find((s) => s.id === row.key);
      const raw = String(row.value ?? "").trim();
      if (raw === "" && normalizeKind(entry?.kind) !== "bool") {
        return { error: `Value required for '${entry?.displayName || row.key}'.` };
      }
      try {
        settings[row.key] = coerceValue(entry, raw);
      } catch (e) {
        return { error: e.message || String(e) };
      }
    }
    if (!Object.keys(settings).length) {
      return { error: "Add at least one setting." };
    }
    return { settings };
  }
}

/** @param {string|number|undefined|null} kind */
function normalizeKind(kind) {
  if (typeof kind === "number") {
    const map = [
      "intseconds", "bool", "string", "datetimeoffset", "enum", "double", "int", "intarray", "stringarray",
    ];
    return map[kind] || "intseconds";
  }
  return String(kind || "IntSeconds").toLowerCase();
}

/**
 * @param {{ kind?: string|number }|undefined} entry
 * @param {string} raw
 */
function coerceValue(entry, raw) {
  const kind = normalizeKind(entry?.kind);
  if (kind === "bool") return raw === "true" || raw === "1";
  if (kind === "enum" || kind === "string") {
    if (!raw) throw new Error("Value is required.");
    return raw;
  }
  if (kind === "datetimeoffset") {
    if (!raw) throw new Error("Date/time is required.");
    const d = new Date(raw);
    if (Number.isNaN(d.getTime())) throw new Error("Invalid date/time.");
    return d.toISOString();
  }
  if (kind === "double") {
    const n = Number(raw);
    if (!Number.isFinite(n)) throw new Error("Number is required.");
    return n;
  }
  const n = Number(raw);
  if (!Number.isFinite(n) || !Number.isInteger(n) || n < 0) {
    throw new Error("Non-negative integer is required.");
  }
  return n;
}
