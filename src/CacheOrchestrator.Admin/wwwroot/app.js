/* CacheOrchestrator Admin UI — multi-view shell (phases A–F) */

const $ = (sel, el = document) => el.querySelector(sel);
const main = () => $("#appMain");

// —— API ——
async function api(path, options = {}) {
  const res = await fetch(path, {
    headers: { "Content-Type": "application/json", ...(options.headers || {}) },
    ...options,
  });
  const text = await res.text();
  let body = null;
  try { body = text ? JSON.parse(text) : null; } catch { body = text; }
  if (!res.ok) {
    const msg = body && body.error ? body.error : (text || res.statusText);
    throw new Error(msg);
  }
  return body;
}

// —— formatting ——
function esc(s) {
  return String(s ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

function pct(rate, lowSample) {
  if (rate == null || Number.isNaN(rate)) return "—";
  const s = (rate * 100).toFixed(1) + "%";
  return lowSample ? `<span class="low-n" title="Low sample (layer n &lt; 20)">${s}</span>` : s;
}

function num(n) {
  if (n == null) return "—";
  return Number(n).toLocaleString();
}

function pipelineBar(p, large) {
  if (!p) return `<div class="pipe empty"></div>`;
  const parts = [
    ["oc", p.ocHitShare, "OC hit"],
    ["fc", p.fcHitShare, "FC hit"],
    ["origin", p.originShare, "Origin"],
    ["bypass", p.bypassShare, "Bypass"],
    ["other", p.otherShare, "Other"],
  ].filter(([, v]) => v != null && v > 0.0005);
  if (!parts.length) return `<div class="pipe empty${large ? " lg" : ""}"></div>`;
  return `<div class="pipe${large ? " lg" : ""}">${
    parts.map(([cls, v, label]) =>
      `<span class="seg ${cls}" style="flex:${Math.max(v, 0.01)}" title="${label}: ${(v * 100).toFixed(1)}%"></span>`
    ).join("")
  }</div>`;
}

function spreadCell(s) {
  if (!s || s.sampleCount < 1) return "—";
  if (s.sampleCount === 1) return pct(s.mean);
  return `${pct(s.min)}–${pct(s.max)} <span class="muted">μ ${pct(s.mean)}</span>`;
}

/**
 * Severity chip stack for aggregates (header / instance / cluster).
 * Empty → neutral ○ (always show a mark).
 */
function severityStack(summary) {
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

/** Compact code badges on entity rows. Empty → ○ mark. */
function hintBadges(hints) {
  if (!hints || !hints.length) {
    return `<span class="hint-badges"><span class="hint empty" title="No recommendations">○</span></span>`;
  }
  return `<span class="hint-badges">${hints.map(h =>
    `<span class="hint ${esc(h.severity || "Info")}" title="${esc(h.message)}">${esc(shortHint(h))}</span>`
  ).join("")}</span>`;
}

// =============================================================================
// REMOVE LATER — Hint mockup (UI preview only)
// Toggle on Hints page; persists in localStorage. Injects fake hints on all pages.
// Delete this whole block + every applyMock* / isHintMock / setHintMock call site
// when real recommendation density is enough for design review.
// Search: "REMOVE LATER — Hint mockup"
// =============================================================================
const HINT_MOCK_KEY = "adminHintMock";
function isHintMock() {
  return localStorage.getItem(HINT_MOCK_KEY) === "1";
}
function setHintMock(on) {
  localStorage.setItem(HINT_MOCK_KEY, on ? "1" : "0");
}

const MOCK_HINT_CATALOG = [
  { severity: "Critical", code: "high-origin-share", message: "Origin/factory share is high — short TTL or frequent misses; consider soft/hard TTL and eager refresh." },
  { severity: "Warning", code: "low-fc-hit-rate", message: "FC layer hit rate below 60% with enough traffic — consider longer Fusion/Output TTL." },
  { severity: "Warning", code: "elevated-stale", message: "Stale serves are elevated — factory failures or fail-safe in use." },
  { severity: "Warning", code: "instance-oc-hit-spread", message: "OC hit share varies across instances — check L1 consistency / uneven traffic." },
  { severity: "Info", code: "client-ttl-gt-output", message: "Client TTL ≫ Output TTL — align the ratio to avoid stale browser cache." },
  { severity: "Info", code: "schedule-phase", message: "Client Cache Schedule is approaching/hold — verify ScheduledUpdateUtc." },
  { severity: "Info", code: "fc-miss-rate-vs-oc-share", message: "FC miss rate looks high only on rare OC misses — prefer request shares." },
];

function mockHintsFor(seed) {
  const n = (seed || 0) % MOCK_HINT_CATALOG.length;
  const count = 1 + (seed % 3);
  const out = [];
  for (let k = 0; k < count; k++) {
    out.push(MOCK_HINT_CATALOG[(n + k) % MOCK_HINT_CATALOG.length]);
  }
  return out;
}

function hashSeed(s) {
  let h = 0;
  const str = String(s || "");
  for (let i = 0; i < str.length; i++) h = ((h << 5) - h + str.charCodeAt(i)) | 0;
  return Math.abs(h);
}

function summarizeHints(hints) {
  let info = 0, warning = 0, critical = 0;
  for (const h of hints || []) {
    if (h.severity === "Critical") critical++;
    else if (h.severity === "Warning") warning++;
    else info++;
  }
  return {
    info, warning, critical,
    total: info + warning + critical,
    maxSeverity: critical ? "Critical" : warning ? "Warning" : info ? "Info" : "None",
  };
}

/** Apply mock hints to API entities when mock mode is on (preview everywhere). */
function applyMockToEndpoint(e, i = 0) {
  if (!isHintMock()) return e;
  const hints = mockHintsFor(hashSeed(e.route) + i);
  return { ...e, hints };
}
function applyMockToDomain(d, i = 0) {
  if (!isHintMock()) return d;
  const hints = mockHintsFor(hashSeed(d.name) + i + 1);
  return {
    ...d,
    hints,
    endpoints: (d.endpoints || []).map((e, j) => applyMockToEndpoint(e, j)),
  };
}
function applyMockToInstance(inst, i = 0) {
  if (!isHintMock()) return inst;
  const hints = mockHintsFor(hashSeed(inst.id) + i + 2);
  return { ...inst, hintSummary: summarizeHints(hints), _mockHints: hints };
}
function applyMockToOverview(o) {
  if (!isHintMock()) return o;
  const top = (o.topEndpoints || []).map((e, i) => applyMockToEndpoint(e, i));
  const instances = (o.instances || []).map((inst, i) => applyMockToInstance(inst, i));
  const all = [];
  top.forEach((e) => all.push(...(e.hints || [])));
  instances.forEach((inst) => all.push(...(inst._mockHints || [])));
  // pad with catalog for demo density
  all.push(...MOCK_HINT_CATALOG);
  return {
    ...o,
    topEndpoints: top,
    instances,
    hintSummary: summarizeHints(all),
    topHints: MOCK_HINT_CATALOG,
  };
}
// =============================================================================
// END REMOVE LATER — Hint mockup
// =============================================================================

function shortHint(h) {
  const map = {
    "low-fc-hit-rate": "FC↓",
    "low-oc-hit-rate": "OC↓",
    "high-origin-share": "Origin↑",
    "elevated-stale": "Stale",
    "very-high-oc-hit-long-ttl": "TTL?",
    "frequent-invalidations": "Inv↑",
    "client-ttl-gt-output": "ClientTTL",
    "schedule-phase": "Sched",
    "instance-oc-hit-spread": "Drift",
    "instance-origin-spread": "Drift",
    "fc-miss-rate-vs-oc-share": "Rate≠share",
  };
  return map[h.code] || (h.severity || "Hint").slice(0, 4);
}

/** Full descriptions — use on detail pages. */
function hintListHtml(hints) {
  if (!hints || !hints.length) return `<p class="muted">No recommendations.</p>`;
  return `<div class="hint-list">${hints.map(h => `
    <div class="hint-row ${esc(h.severity || "Info")}">
      <span class="hint-sev">${esc(h.severity || "Info")}</span>
      <div>
        <div class="hint-code"><code>${esc(h.code)}</code></div>
        <div class="hint-msg">${esc(h.message)}</div>
      </div>
    </div>`).join("")}</div>`;
}

function parseCsvParam(params, key) {
  const raw = params.get(key) || "";
  if (!raw) return null; // null = All (no filter)
  if (raw === "__none__") return []; // explicit none
  return raw.split(",").map((s) => s.trim()).filter(Boolean);
}

function csvParamFromSelection(ids) {
  // null/undefined → omit (All). [] → none. [...] → filter.
  if (ids === null || ids === undefined) return "";
  if (ids.length === 0) return "__none__";
  return ids.join(",");
}

/**
 * Multi-select: mode All | filter | none.
 * selectedIds: null = All, [] = none, [id,...] = explicit filter (even if all items checked).
 */
function multiSelectHtml(id, label, options, selectedIds) {
  const mode = selectedIds === null || selectedIds === undefined
    ? "all"
    : selectedIds.length === 0
      ? "none"
      : "filter";
  let summary = "All";
  if (mode === "none") summary = "None";
  else if (mode === "filter") {
    summary = selectedIds.length <= 2
      ? selectedIds.join(", ")
      : `${selectedIds.length} selected`;
  }
  return `
    <div class="ms" data-ms="${esc(id)}">
      <span class="ms-label">${esc(label)}</span>
      <button type="button" class="ms-btn" data-ms-toggle="${esc(id)}">${esc(summary)} ▾</button>
      <div class="ms-panel hidden" data-ms-panel="${esc(id)}" data-ms-mode="${mode}">
        <div class="ms-actions">
          <button type="button" class="secondary" data-ms-all="${esc(id)}">All</button>
          <button type="button" class="secondary" data-ms-none="${esc(id)}">None</button>
        </div>
        ${options.map((o) => {
          const checked = mode === "all" || (mode === "filter" && selectedIds.includes(o.id));
          return `<label><input type="checkbox" value="${esc(o.id)}" ${checked ? "checked" : ""}/> ${esc(o.label)}</label>`;
        }).join("")}
      </div>
    </div>`;
}

function bindMultiSelects(root) {
  root.querySelectorAll("[data-ms-toggle]").forEach((btn) => {
    btn.addEventListener("click", (ev) => {
      ev.preventDefault();
      ev.stopPropagation();
      const id = btn.dataset.msToggle;
      const panel = root.querySelector(`[data-ms-panel="${id}"]`);
      root.querySelectorAll("[data-ms-panel]").forEach((p) => {
        if (p !== panel) p.classList.add("hidden");
      });
      panel?.classList.toggle("hidden");
    });
  });
  root.querySelectorAll("[data-ms-all]").forEach((btn) => {
    btn.addEventListener("click", (ev) => {
      ev.preventDefault();
      const id = btn.dataset.msAll;
      const panel = root.querySelector(`[data-ms-panel="${id}"]`);
      if (panel) panel.dataset.msMode = "all";
      root.querySelectorAll(`[data-ms-panel="${id}"] input[type=checkbox]`).forEach((c) => { c.checked = true; });
      updateMsSummary(root, id);
    });
  });
  root.querySelectorAll("[data-ms-none]").forEach((btn) => {
    btn.addEventListener("click", (ev) => {
      ev.preventDefault();
      const id = btn.dataset.msNone;
      const panel = root.querySelector(`[data-ms-panel="${id}"]`);
      if (panel) panel.dataset.msMode = "none";
      root.querySelectorAll(`[data-ms-panel="${id}"] input[type=checkbox]`).forEach((c) => { c.checked = false; });
      updateMsSummary(root, id);
    });
  });
  root.querySelectorAll("[data-ms-panel] input[type=checkbox]").forEach((cb) => {
    cb.addEventListener("change", () => {
      const panel = cb.closest("[data-ms-panel]");
      const id = panel?.dataset.msPanel;
      if (panel) panel.dataset.msMode = "filter";
      if (id) updateMsSummary(root, id);
    });
  });
  if (!window.__msOutsideBound) {
    window.__msOutsideBound = true;
    document.addEventListener("click", closeMsOutside);
  }
}

function closeMsOutside(ev) {
  if (ev.target.closest("[data-ms]")) return;
  document.querySelectorAll("[data-ms-panel]").forEach((p) => p.classList.add("hidden"));
}

function updateMsSummary(root, id) {
  const panel = root.querySelector(`[data-ms-panel="${id}"]`);
  const btn = root.querySelector(`[data-ms-toggle="${id}"]`);
  if (!panel || !btn) return;
  const mode = panel.dataset.msMode || "all";
  const boxes = [...panel.querySelectorAll("input[type=checkbox]")];
  const checked = boxes.filter((c) => c.checked).map((c) => c.value);
  let summary = "All";
  if (mode === "none" || (mode === "filter" && checked.length === 0)) summary = "None";
  else if (mode === "filter") {
    summary = checked.length <= 2 ? checked.join(", ") : `${checked.length} selected`;
  }
  btn.textContent = summary + " ▾";
}

/**
 * @returns {null|string[]} null = All (no filter), [] = none, [...] = filter
 */
function readMultiSelect(root, id) {
  const panel = root.querySelector(`[data-ms-panel="${id}"]`);
  if (!panel) return null;
  const mode = panel.dataset.msMode || "all";
  if (mode === "all") return null;
  const boxes = [...panel.querySelectorAll("input[type=checkbox]")];
  const checked = boxes.filter((c) => c.checked).map((c) => c.value);
  if (mode === "none") return [];
  return checked; // filter mode: even if all items checked, keep explicit list
}

// —— ONE list view per entity type (same columns everywhere) ——
//
// Endpoints columns (fixed): Route | Domain | Hints | Req | Pipeline | OC hit share | Origin | FC miss rate | Stale
// Domains columns (fixed):   Domain | Hints | Version | Req | Pipeline | OC hit share | Origin | Inv | Ops
// Instances columns (fixed): Id | Hints | Status | URL | Latency | Error
//
// Exception: none for list surfaces. Detail pages may add sections below the table.

function endpointRowHtml(e) {
  const domainCell = e.configuredDomain
    ? `<a href="#/domains?name=${encodeURIComponent(e.configuredDomain)}">${esc(e.configuredDomain)}</a>`
    : "—";
  return `<tr class="clickable entity-row" data-entity="endpoint" data-route="${esc(e.route)}">
    <td class="col-name"><code>${esc(e.route)}</code></td>
    <td class="col-domain">${domainCell}</td>
    <td class="col-hints">${hintBadges(e.hints)}</td>
    <td class="col-num">${num(e.requests)}</td>
    <td class="col-pipe">${pipelineBar(e.pipeline)}</td>
    <td class="col-metric">${pct(e.oc?.hitShare, e.oc?.lowSample)}</td>
    <td class="col-metric">${pct(e.fc?.originShare, e.fc?.lowSample)}</td>
    <td class="col-metric secondary">${pct(e.fc?.missRate, e.fc?.lowSample)}</td>
    <td class="col-metric secondary">${num(e.fc?.stale)}</td>
  </tr>`;
}

function endpointTableHtml(list) {
  if (!list || !list.length) return `<p class="empty">No endpoints</p>`;
  return `
    <table class="dense entity-table endpoints-table">
      <thead>
        <tr>
          <th>Route</th>
          <th>Domain</th>
          <th>Hints</th>
          <th>Req</th>
          <th>Pipeline</th>
          <th>OC hit share</th>
          <th>Origin</th>
          <th class="secondary">FC miss rate</th>
          <th class="secondary">Stale</th>
        </tr>
      </thead>
      <tbody>${list.map(endpointRowHtml).join("")}</tbody>
    </table>`;
}

function domainRowHtml(d) {
  return `<tr class="clickable entity-row" data-entity="domain" data-name="${esc(d.name)}">
    <td class="col-name"><code>${esc(d.name)}</code>${d.versionIsRuntimeOverride ? ' <span class="badge">rt</span>' : ""}</td>
    <td class="col-hints">${hintBadges(d.hints)}</td>
    <td class="col-metric">${esc(d.version)}</td>
    <td class="col-num">${num(d.requests)}</td>
    <td class="col-pipe">${pipelineBar(d.pipeline)}</td>
    <td class="col-metric">${pct(d.oc?.hitShare, d.oc?.lowSample)}</td>
    <td class="col-metric">${pct(d.fc?.originShare)}</td>
    <td class="col-num">${num(d.invalidations)}</td>
    <td class="col-ops"><a href="#/operations?domain=${encodeURIComponent(d.name)}" onclick="event.stopPropagation()">Ops</a></td>
  </tr>`;
}

function domainTableHtml(list) {
  if (!list || !list.length) return `<p class="empty">No domains</p>`;
  return `
    <table class="dense entity-table domains-table">
      <thead>
        <tr>
          <th>Domain</th>
          <th>Hints</th>
          <th>Version</th>
          <th>Req</th>
          <th>Pipeline</th>
          <th>OC hit share</th>
          <th>Origin</th>
          <th>Inv</th>
          <th></th>
        </tr>
      </thead>
      <tbody>${list.map(domainRowHtml).join("")}</tbody>
    </table>`;
}

function formatUptime(seconds) {
  if (seconds == null || seconds < 0 || Number.isNaN(Number(seconds))) return "—";
  const s = Math.floor(Number(seconds));
  const d = Math.floor(s / 86400);
  const h = Math.floor((s % 86400) / 3600);
  const m = Math.floor((s % 3600) / 60);
  if (d > 0) return `${d}d ${h}h`;
  if (h > 0) return `${h}h ${m}m`;
  if (m > 0) return `${m}m`;
  return `${s}s`;
}

function instanceRowHtml(i) {
  const started = i.startedAtUtc
    ? new Date(i.startedAtUtc).toISOString().replace("T", " ").replace(/\.\d+Z$/, "Z")
    : "";
  const up = formatUptime(i.uptimeSeconds);
  return `<tr class="clickable entity-row" data-entity="instance" data-id="${esc(i.id)}">
    <td class="col-name"><code>${esc(i.id)}</code></td>
    <td class="col-hints">${severityStack(i.hintSummary)}</td>
    <td class="status-${esc(i.status)}">${esc(i.status)}</td>
    <td><code>${esc(i.url)}</code></td>
    <td title="${esc(started || "start time unknown")}">${esc(up)}</td>
    <td>${i.latencyMs != null ? Math.round(i.latencyMs) + " ms" : "—"}</td>
    <td class="muted">${esc(i.error || "—")}</td>
  </tr>`;
}

function instanceTableHtml(list) {
  if (!list || !list.length) return `<p class="empty">No instances</p>`;
  return `
    <table class="dense entity-table instances-table">
      <thead>
        <tr>
          <th>Id</th>
          <th>Hints</th>
          <th>Status</th>
          <th>URL</th>
          <th>Uptime</th>
          <th>Latency</th>
          <th>Error</th>
        </tr>
      </thead>
      <tbody>${list.map(instanceRowHtml).join("")}</tbody>
    </table>`;
}

function bindEntityTableClicks(root) {
  root.querySelectorAll("tr.entity-row[data-entity=endpoint]").forEach((tr) => {
    tr.addEventListener("click", (ev) => {
      if (ev.target.closest("a")) return;
      navigate("endpoints", { route: tr.dataset.route });
    });
  });
  root.querySelectorAll("tr.entity-row[data-entity=domain]").forEach((tr) => {
    tr.addEventListener("click", (ev) => {
      if (ev.target.closest("a")) return;
      navigate("domains", { name: tr.dataset.name });
    });
  });
  root.querySelectorAll("tr.entity-row[data-entity=instance]").forEach((tr) => {
    tr.addEventListener("click", () => navigate("instances", { id: tr.dataset.id }));
  });
}

// —— router ——
function parseHash() {
  const raw = (location.hash || "#/overview").replace(/^#\/?/, "");
  const [pathPart, queryPart] = raw.split("?");
  const path = (pathPart || "overview").replace(/\/$/, "") || "overview";
  const params = new URLSearchParams(queryPart || "");
  return { path, params };
}

function navigate(path, params = {}) {
  const q = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    if (v != null && v !== "") q.set(k, v);
  }
  const qs = q.toString();
  location.hash = "#/" + path + (qs ? "?" + qs : "");
}

function setNavActive(path) {
  const root = path.split("/")[0] || "overview";
  document.querySelectorAll(".app-nav a").forEach((a) => {
    a.classList.toggle("active", a.dataset.nav === root);
  });
}

function setBreadcrumb(parts) {
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

// —— header (A) ——
let headerTimer = null;
let lastOverview = null;

async function refreshHeader() {
  try {
    let o = await api("/api/overview");
    o = applyMockToOverview(o);
    lastOverview = o;
    renderHeader(o);
    updateNavHintsBadge(o.hintSummary);
  } catch (err) {
    $("#headerMetrics").innerHTML =
      `<span class="hm status-Down">Header: ${esc(err.message)}</span>`;
  }
}

function updateNavHintsBadge(summary) {
  const el = document.querySelector("[data-nav-hints-badge]");
  if (!el) return;
  el.innerHTML = severityStack(summary || { total: 0 });
}

function renderHeader(o) {
  const healthDots = [
    ...Array(o.healthyCount || 0).fill("ok"),
    ...Array(o.degradedCount || 0).fill("warn"),
    ...Array(o.downCount || 0).fill("bad"),
  ].map((c) => `<span class="dot ${c}"></span>`).join("") || `<span class="muted">no instances</span>`;

  const hs = o.hintSummary || { total: 0 };

  $("#headerMetrics").innerHTML = `
    <span class="hm" title="Instance health">${healthDots}
      <strong>${o.healthyCount ?? 0}</strong><span class="muted">up</span>
      ${(o.downCount || 0) > 0 ? `<span class="status-Down">${o.downCount} down</span>` : ""}
    </span>
    <span class="hm" title="Cluster recommendation urgency">${severityStack(hs)}</span>
    <span class="hm" title="Request pipeline">${pipelineBar(o.pipeline)}</span>
    <span class="hm">OC hit <strong>${pct(o.ocHitShare)}</strong></span>
    <span class="hm">Origin <strong>${pct(o.originShare)}</strong></span>
    <span class="hm">Req <strong>${num(o.totalRequests)}</strong></span>
    <span class="hm">Inv <strong>${num(o.totalInvalidations)}</strong></span>
    ${isHintMock() ? `<span class="hm badge" title="Mock hints active">MOCK</span>` : ""}
    ${(o.alerts && o.alerts.length) ? `<span class="hm status-Degraded" title="${esc(o.alerts.join(" | "))}">⚠ ${o.alerts.length}</span>` : ""}
  `;
}

// —— shared layer detail ——
function layerDetailOc(oc) {
  if (!oc) return "";
  return `
    <div class="detail-block">
      <h3>Output Cache</h3>
      <div class="kv">
        <span>Hits</span><span>${num(oc.hits)}</span>
        <span>Misses</span><span>${num(oc.misses)}</span>
        <span>Bypass</span><span>${num(oc.bypass)}</span>
        <span>Layer n</span><span>${num(oc.layerSampleSize)}</span>
        <span>Hit share</span><span>${pct(oc.hitShare, oc.lowSample)}</span>
        <span>Miss share</span><span>${pct(oc.missShare, oc.lowSample)}</span>
        <span>Bypass share</span><span>${pct(oc.bypassShare)}</span>
        <span>Hit rate (layer)</span><span>${pct(oc.hitRate, oc.lowSample)}</span>
        <span>Miss rate (layer)</span><span>${pct(oc.missRate, oc.lowSample)}</span>
      </div>
    </div>`;
}

function layerDetailFc(fc) {
  if (!fc) return "";
  return `
    <div class="detail-block">
      <h3>FusionCache</h3>
      <div class="kv">
        <span>Hits</span><span>${num(fc.hits)}</span>
        <span>Misses</span><span>${num(fc.misses)}</span>
        <span>Stale</span><span>${num(fc.stale)}</span>
        <span>Bypass</span><span>${num(fc.bypass)}</span>
        <span>Factory runs</span><span>${num(fc.factoryRuns)}</span>
        <span>Factory failures</span><span>${num(fc.factoryFailures)}</span>
        <span>Layer n</span><span>${num(fc.layerSampleSize)}</span>
        <span>Hit share</span><span>${pct(fc.hitShare, fc.lowSample)}</span>
        <span>Miss share</span><span>${pct(fc.missShare, fc.lowSample)}</span>
        <span>Stale share</span><span>${pct(fc.staleShare)}</span>
        <span>Origin share</span><span>${pct(fc.originShare)}</span>
        <span>Hit rate (layer)</span><span>${pct(fc.hitRate, fc.lowSample)}</span>
        <span>Miss rate (layer)</span><span>${pct(fc.missRate, fc.lowSample)}</span>
        <span>Stale rate (layer)</span><span>${pct(fc.staleRate)}</span>
      </div>
    </div>`;
}

// —— B: Overview ——
async function renderOverview() {
  setBreadcrumb([{ label: "Overview" }]);
  main().innerHTML = `<p class="muted">Loading overview…</p>`;
  let o = await api("/api/overview");
  o = applyMockToOverview(o);
  lastOverview = o;
  renderHeader(o);
  updateNavHintsBadge(o.hintSummary);

  main().innerHTML = `
    <div class="kpi-row">
      <div class="kpi"><div class="label">Instances up</div><div class="value status-Healthy">${o.healthyCount}/${(o.instances||[]).length}</div></div>
      <div class="kpi"><div class="label">Cluster hints</div><div class="value">${severityStack(o.hintSummary)}</div></div>
      <div class="kpi"><div class="label">Requests</div><div class="value">${num(o.totalRequests)}</div></div>
      <div class="kpi"><div class="label">OC hit share</div><div class="value">${pct(o.ocHitShare)}</div></div>
      <div class="kpi"><div class="label">Origin share</div><div class="value">${pct(o.originShare)}</div></div>
      <div class="kpi"><div class="label">Domains / EP</div><div class="value" style="font-size:1rem">${num(o.domainCount)} / ${num(o.endpointCount)}</div></div>
    </div>
    <div class="card">
      <h2>Cluster pipeline</h2>
      ${pipelineBar(o.pipeline, true)}
      <p class="muted" style="margin:0.5rem 0 0;font-size:0.85rem">OC hit · FC hit · Origin · Bypass — shares of total requests</p>
    </div>
    ${o.alerts?.length ? `<div class="card"><h2>Alerts</h2><ul class="alert-list">${o.alerts.map(a => `<li>${esc(a)}</li>`).join("")}</ul></div>` : ""}
    <div class="card">
      <h2>Instances</h2>
      ${instanceTableHtml(o.instances || [])}
    </div>
    <div class="card">
      <h2>Endpoints <span class="badge">top by origin / traffic</span></h2>
      ${endpointTableHtml(o.topEndpoints || [])}
      <p style="margin:0.75rem 0 0"><a href="#/endpoints">All endpoints →</a></p>
    </div>`;

  bindEntityTableClicks(main());
}

// —— C: Endpoints list + detail ——
async function renderEndpointsList(params) {
  setBreadcrumb([{ label: "Endpoints", href: "#/endpoints" }]);
  const search = params.get("search") || "";
  const sort = params.get("sort") || "requests";
  const minRequests = params.get("minRequests") || "0";
  const take = params.get("take") || "50";
  const skip = Number(params.get("skip") || "0");
  // null = All, [] = none, [...] = filter
  const selInstances = parseCsvParam(params, "instances");
  const selDomains = parseCsvParam(params, "domains");

  main().innerHTML = `<div class="card"><h2>Endpoints <span class="badge">primary unit</span></h2>
    <p class="muted">Loading filters…</p></div>`;

  const [instanceList, statsForFilters] = await Promise.all([
    api("/api/instances"),
    api("/api/stats?scope=all"),
  ]);
  const domainOpts = (statsForFilters.domains || []).map((d) => ({ id: d.name, label: d.name }));
  const instanceOpts = (instanceList || []).map((i) => ({ id: i.id, label: i.id }));

  main().innerHTML = `
    <div class="card">
      <h2>Endpoints <span class="badge">primary unit</span></h2>
      <p class="muted" style="margin-top:0">Filter: <strong>All</strong> = no filter. Explicit checks apply even for a single domain. <strong>None</strong> = empty list.</p>
      <form class="toolbar" id="epFilters">
        <label>Search<input name="search" value="${esc(search)}" placeholder="route or domain" /></label>
        ${multiSelectHtml("epInst", "Instances", instanceOpts, selInstances)}
        ${multiSelectHtml("epDom", "Domains", domainOpts, selDomains)}
        <label>Min requests<input name="minRequests" type="number" min="0" value="${esc(minRequests)}" /></label>
        <label>Sort
          <select name="sort">
            ${["requests","originShare","ocHitShare","fcMissShare","fcMissRate","route","stale"].map(s =>
              `<option value="${s}" ${s===sort?"selected":""}>${s}</option>`).join("")}
          </select>
        </label>
        <button type="submit">Apply</button>
      </form>
      <div id="epTable"><p class="muted">Loading…</p></div>
    </div>`;

  const form = $("#epFilters");
  bindMultiSelects(form);

  form.addEventListener("submit", (ev) => {
    ev.preventDefault();
    const fd = new FormData(form);
    navigate("endpoints", {
      search: fd.get("search"),
      instances: csvParamFromSelection(readMultiSelect(form, "epInst")),
      domains: csvParamFromSelection(readMultiSelect(form, "epDom")),
      minRequests: fd.get("minRequests"),
      sort: fd.get("sort"),
      take,
      skip: 0,
    });
  });

  const q = new URLSearchParams({ sort, take, skip: String(skip), search, minRequests });
  if (selInstances !== null) q.set("instances", selInstances.length ? selInstances.join(",") : "__none__");
  if (selDomains !== null) q.set("domains", selDomains.length ? selDomains.join(",") : "__none__");

  let list = [];
  if (selDomains !== null && selDomains.length === 0) {
    list = [];
  } else if (selInstances !== null && selInstances.length === 0) {
    list = [];
  } else {
    list = await api("/api/endpoints?" + q.toString());
  }
  list = list.map((e, i) => applyMockToEndpoint(e, i));

  $("#epTable").innerHTML = endpointTableHtml(list) + `
    <div class="pager">
      <button type="button" class="secondary" id="epPrev" ${skip<=0?"disabled":""}>Prev</button>
      <span>skip ${skip} · ${list.length} rows</span>
      <button type="button" class="secondary" id="epNext" ${list.length < Number(take)?"disabled":""}>Next</button>
    </div>`;

  bindEntityTableClicks($("#epTable"));
  const pageParams = () => ({
    search, sort, minRequests, take,
    instances: csvParamFromSelection(selInstances),
    domains: csvParamFromSelection(selDomains),
  });
  $("#epPrev")?.addEventListener("click", () => navigate("endpoints", {
    ...pageParams(), skip: Math.max(0, skip - Number(take)),
  }));
  $("#epNext")?.addEventListener("click", () => navigate("endpoints", {
    ...pageParams(), skip: skip + Number(take),
  }));
}

async function renderEndpointDetail(route) {
  setBreadcrumb([
    { label: "Endpoints", href: "#/endpoints" },
    { label: route },
  ]);
  main().innerHTML = `<p class="muted">Loading ${esc(route)}…</p>`;

  const stats = await api("/api/stats?scope=all&groupByInstance=true");
  let ep = (stats.endpoints || []).find((e) => e.route === route);
  if (!ep) {
    main().innerHTML = `<div class="card"><p class="status-Down">Endpoint not found: <code>${esc(route)}</code></p>
      <a href="#/endpoints">← Back</a></div>`;
    return;
  }
  ep = applyMockToEndpoint(ep);

  main().innerHTML = `
    <div class="card">
      <h2><code>${esc(ep.route)}</code>
        ${ep.configuredDomain ? `<a class="badge" href="#/domains?name=${encodeURIComponent(ep.configuredDomain)}">${esc(ep.configuredDomain)}</a>` : ""}
        ${hintBadges(ep.hints)}
      </h2>
      <h3 class="section-sub">Recommendations</h3>
      ${hintListHtml(ep.hints)}
      <div class="kpi-row">
        <div class="kpi"><div class="label">Requests</div><div class="value">${num(ep.requests)}</div></div>
        <div class="kpi"><div class="label">OC hit share</div><div class="value">${pct(ep.oc?.hitShare, ep.oc?.lowSample)}</div></div>
        <div class="kpi"><div class="label">Origin share</div><div class="value">${pct(ep.fc?.originShare)}</div></div>
        <div class="kpi"><div class="label">FC stale</div><div class="value">${num(ep.fc?.stale)}</div></div>
      </div>
      <p class="muted">Pipeline</p>
      ${pipelineBar(ep.pipeline, true)}
    </div>
    <div class="detail-grid">
      ${layerDetailOc(ep.oc)}
      ${layerDetailFc(ep.fc)}
    </div>
    ${ep.byInstance?.length ? `
    <div class="card">
      <h2>By instance <span class="badge">spread</span></h2>
      ${ep.instanceSpread ? `<p class="muted">OC hit share ${spreadCell(ep.instanceSpread.ocHitShare)} · Origin ${spreadCell(ep.instanceSpread.originShare)}</p>` : ""}
      <table class="dense">
        <thead><tr><th>Instance</th><th>Req</th><th>OC hit share</th><th>FC hit share</th><th>Origin</th><th>Stale</th><th>Factory</th></tr></thead>
        <tbody>
          ${ep.byInstance.map(bi => `
            <tr class="clickable" data-id="${esc(bi.instanceId)}">
              <td><code>${esc(bi.instanceId)}</code></td>
              <td>${num(bi.requests)}</td>
              <td>${pct(bi.oc?.hitShare, bi.oc?.lowSample)}</td>
              <td>${pct(bi.fc?.hitShare, bi.fc?.lowSample)}</td>
              <td>${pct(bi.fc?.originShare)}</td>
              <td>${num(bi.fc?.stale)}</td>
              <td>${num(bi.fc?.factoryRuns)}</td>
            </tr>`).join("")}
        </tbody>
      </table>
    </div>` : ""}
    <p><a href="#/endpoints">← All endpoints</a>
      ${ep.configuredDomain ? ` · <a href="#/operations?domain=${encodeURIComponent(ep.configuredDomain)}">Operations for domain</a>` : ""}
    </p>`;

  main().querySelectorAll("tr.clickable[data-id]").forEach((tr) => {
    tr.addEventListener("click", () => navigate("instances", { id: tr.dataset.id }));
  });
}

// —— D: Domains ——
async function renderDomainsList(params) {
  setBreadcrumb([{ label: "Domains", href: "#/domains" }]);
  const selInstances = parseCsvParam(params, "instances"); // null=all, []=none

  main().innerHTML = `<div class="card"><p class="muted">Loading domains…</p></div>`;
  const instanceList = await api("/api/instances");
  const instanceOpts = (instanceList || []).map((i) => ({ id: i.id, label: i.id }));

  let domains = [];
  if (selInstances !== null && selInstances.length === 0) {
    domains = [];
  } else {
    const q = new URLSearchParams({ scope: "all" });
    if (selInstances !== null) q.set("instances", selInstances.join(","));
    const stats = await api("/api/stats?" + q.toString());
    domains = (stats.domains || []).map((d, i) => applyMockToDomain(d, i));
  }

  main().innerHTML = `
    <div class="card">
      <h2>Domains</h2>
      <form class="toolbar" id="domFilters">
        ${multiSelectHtml("domInst", "Instances", instanceOpts, selInstances)}
        <button type="submit">Apply</button>
      </form>
      ${domainTableHtml(domains)}
    </div>`;

  const form = $("#domFilters");
  bindMultiSelects(form);
  form.addEventListener("submit", (ev) => {
    ev.preventDefault();
    navigate("domains", { instances: csvParamFromSelection(readMultiSelect(form, "domInst")) });
  });

  bindEntityTableClicks(main());
}

async function renderDomainDetail(name) {
  setBreadcrumb([
    { label: "Domains", href: "#/domains" },
    { label: name },
  ]);
  main().innerHTML = `<p class="muted">Loading domain ${esc(name)}…</p>`;

  const [stats, cfgFan] = await Promise.all([
    api("/api/stats?scope=all&groupByInstance=true"),
    api("/api/domains"),
  ]);
  let d = (stats.domains || []).find((x) => x.name === name);
  const cfg = (cfgFan.data || []).find((x) => x.name === name);

  if (!d && !cfg) {
    main().innerHTML = `<div class="card"><p class="status-Down">Domain not found</p></div>`;
    return;
  }

  let domain = d || { name, requests: 0, oc: {}, fc: {}, pipeline: {}, endpoints: [], hints: [] };
  domain = applyMockToDomain(domain);

  main().innerHTML = `
    <div class="card">
      <h2><code>${esc(name)}</code>
        ${domain.versionIsRuntimeOverride ? '<span class="badge">runtime version</span>' : ""}
        ${hintBadges(domain.hints)}
        <a class="badge" href="#/operations?domain=${encodeURIComponent(name)}">Operations</a>
      </h2>
      <h3 class="section-sub">Recommendations</h3>
      ${hintListHtml(domain.hints)}
      <div class="kpi-row">
        <div class="kpi"><div class="label">Version</div><div class="value" style="font-size:1rem">${esc(domain.version || cfg?.version || "—")}</div></div>
        <div class="kpi"><div class="label">Requests</div><div class="value">${num(domain.requests)}</div></div>
        <div class="kpi"><div class="label">OC hit share</div><div class="value">${pct(domain.oc?.hitShare, domain.oc?.lowSample)}</div></div>
        <div class="kpi"><div class="label">Origin share</div><div class="value">${pct(domain.fc?.originShare)}</div></div>
        <div class="kpi"><div class="label">Invalidations</div><div class="value">${num(domain.invalidations)}</div></div>
      </div>
      ${pipelineBar(domain.pipeline, true)}
    </div>
    <div class="detail-grid">
      ${layerDetailOc(domain.oc)}
      ${layerDetailFc(domain.fc)}
      ${cfg ? `
      <div class="detail-block">
        <h3>Effective config</h3>
        <div class="kv">
          <span>Output TTL s</span><span>${cfg.outputCacheTtlSeconds}</span>
          <span>Fusion soft/hard</span><span>${cfg.fusionCacheSoftTtlSeconds} / ${cfg.fusionCacheHardTtlSeconds}</span>
          <span>Fail-safe s</span><span>${cfg.fusionCacheFailSafeSeconds}</span>
          <span>Client TTL / min</span><span>${cfg.clientTtlSeconds} / ${cfg.clientTtlMinSeconds}</span>
          <span>Schedule phase</span><span>${esc(cfg.schedulePhase || "—")}</span>
          <span>FC instance</span><span>${esc(cfg.fusionCacheInstanceName)}</span>
        </div>
      </div>` : ""}
    </div>
    ${domain.byInstance?.length ? `
    <div class="card">
      <h2>By instance</h2>
      ${domain.instanceSpread ? `<p class="muted">OC hit ${spreadCell(domain.instanceSpread.ocHitShare)} · FC hit ${spreadCell(domain.instanceSpread.fcHitShare)}</p>` : ""}
      <table class="dense">
        <thead><tr><th>Instance</th><th>Version</th><th>Req</th><th>OC hit share</th><th>Origin</th><th>Inv</th></tr></thead>
        <tbody>
          ${domain.byInstance.map(bi => `
            <tr class="clickable" data-id="${esc(bi.instanceId)}">
              <td><code>${esc(bi.instanceId)}</code></td>
              <td>${esc(bi.version)}${bi.versionIsRuntimeOverride ? " *" : ""}</td>
              <td>${num(bi.requests)}</td>
              <td>${pct(bi.oc?.hitShare, bi.oc?.lowSample)}</td>
              <td>${pct(bi.fc?.originShare)}</td>
              <td>${num(bi.invalidations)}</td>
            </tr>`).join("")}
        </tbody>
      </table>
    </div>` : ""}
    <div class="card">
      <h2>Endpoints in domain</h2>
      ${endpointTableHtml(domain.endpoints || [])}
    </div>
    <p><a href="#/domains">← Domains</a> · <a href="#/operations?domain=${encodeURIComponent(name)}">Operations</a></p>`;

  main().querySelectorAll("tr.clickable[data-id]").forEach((tr) => {
    tr.addEventListener("click", () => navigate("instances", { id: tr.dataset.id }));
  });
  bindEntityTableClicks(main());
}

// —— E: Instances ——
async function renderInstancesList() {
  setBreadcrumb([{ label: "Instances", href: "#/instances" }]);
  main().innerHTML = `<p class="muted">Loading…</p>`;
  let overview = await api("/api/overview");
  overview = applyMockToOverview(overview);
  renderHeader(overview);
  updateNavHintsBadge(overview.hintSummary);
  const list = overview.instances || [];

  main().innerHTML = `
    <div class="card">
      <h2>Instances ${severityStack(overview.hintSummary)}</h2>
      ${instanceTableHtml(list)}
    </div>`;

  bindEntityTableClicks(main());
}

async function renderInstanceDetail(id) {
  setBreadcrumb([
    { label: "Instances", href: "#/instances" },
    { label: id },
  ]);
  main().innerHTML = `<p class="muted">Loading instance ${esc(id)}…</p>`;

  const [instances, stats] = await Promise.all([
    api("/api/instances"),
    api(`/api/stats?scope=instance:${encodeURIComponent(id)}`),
  ]);
  const inst = instances.find((i) => i.id === id);
  const startedTitle = inst?.startedAtUtc
    ? new Date(inst.startedAtUtc).toISOString()
    : "";

  main().innerHTML = `
    <div class="card">
      <h2>Instance <code>${esc(id)}</code>
        <span class="status-${esc(inst?.status || "Down")}">${esc(inst?.status || "unknown")}</span>
        ${severityStack(inst?.hintSummary)}
      </h2>
      <p class="muted"><code>${esc(inst?.url || "")}</code>
        · reported <code>${esc(inst?.reportedInstanceId || "—")}</code>
        · latency ${inst?.latencyMs != null ? Math.round(inst.latencyMs) + " ms" : "—"}
        ${inst?.error ? ` · <span class="status-Down">${esc(inst.error)}</span>` : ""}
      </p>
      <div class="kpi-row">
        <div class="kpi" title="${esc(startedTitle)}"><div class="label">Uptime</div><div class="value" style="font-size:1.05rem">${esc(formatUptime(inst?.uptimeSeconds))}</div></div>
        <div class="kpi"><div class="label">Started (UTC)</div><div class="value" style="font-size:0.85rem">${esc(startedTitle ? startedTitle.replace("T", " ").replace(/\.\d+Z$/, "Z") : "—")}</div></div>
        <div class="kpi"><div class="label">Domains</div><div class="value">${(stats.domains||[]).length}</div></div>
        <div class="kpi"><div class="label">Endpoints</div><div class="value">${(stats.endpoints||[]).length}</div></div>
        <div class="kpi"><div class="label">Requests</div><div class="value">${num((stats.domains||[]).reduce((s,d)=>s+(d.requests||0),0))}</div></div>
      </div>
    </div>
    <div class="card">
      <h2>Domains on instance</h2>
      ${domainTableHtml((stats.domains || []).map((d, i) => applyMockToDomain(d, i)))}
    </div>
    <div class="card">
      <h2>Endpoints on instance</h2>
      ${endpointTableHtml((stats.endpoints || []).slice(0, 50).map((e, i) => applyMockToEndpoint(e, i)))}
    </div>
    <p><a href="#/instances">← Instances</a>
      · <a href="#/operations?target=instance:${encodeURIComponent(id)}">Operations on this instance</a></p>`;

  bindEntityTableClicks(main());
}

/**
 * Collect flat hint rows from cluster stats for Hints page.
 * Each row: { severity, code, message, instanceId, domain, route, entityType }
 */
function collectHintRows(stats, opts = {}) {
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

  for (const d of stats.domains || []) {
    const dHints = isHintMock() ? applyMockToDomain(d).hints : (d.hints || []);
    for (const h of dHints) {
      push(h, { domain: d.name, instanceId: d.instanceId || "", entityType: "domain" });
    }
    if (d.byInstance) {
      for (const bi of d.byInstance) {
        const biHints = isHintMock() ? applyMockToDomain(bi).hints : (bi.hints || []);
        for (const h of biHints) {
          push(h, { domain: d.name, instanceId: bi.instanceId || "", entityType: "domain" });
        }
      }
    }
  }

  for (const e of stats.endpoints || []) {
    const eHints = isHintMock() ? applyMockToEndpoint(e).hints : (e.hints || []);
    for (const h of eHints) {
      push(h, {
        domain: e.configuredDomain || "",
        route: e.route,
        instanceId: e.instanceId || "",
        entityType: "endpoint",
      });
    }
    if (e.byInstance) {
      for (const bi of e.byInstance) {
        const biHints = isHintMock() ? applyMockToEndpoint(bi).hints : (bi.hints || []);
        for (const h of biHints) {
          push(h, {
            domain: e.configuredDomain || bi.configuredDomain || "",
            route: e.route,
            instanceId: bi.instanceId || "",
            entityType: "endpoint",
          });
        }
      }
    }
  }

  // Mock-only extra catalog rows so Hints page is never empty in mock mode
  if (isHintMock() && opts.includeCatalog !== false) {
    for (const h of MOCK_HINT_CATALOG) {
      push(h, { domain: "catalog", route: "GET /api/products/{id}", instanceId: "app-1", entityType: "endpoint" });
      push(h, { domain: "hello", route: "GET /hello", instanceId: "local-minimal", entityType: "endpoint" });
    }
  }

  return rows;
}

// —— Hints page (final) ——
async function renderHintsPage(params) {
  setBreadcrumb([{ label: "Hints" }]);
  const selInstances = parseCsvParam(params, "instances");
  const selDomains = parseCsvParam(params, "domains");
  const selEndpoints = parseCsvParam(params, "endpoints");
  const severity = params.get("severity") || "";

  main().innerHTML = `<div class="card"><p class="muted">Loading hints…</p></div>`;

  const [instanceList, stats] = await Promise.all([
    api("/api/instances"),
    api("/api/stats?scope=all&groupByInstance=true"),
  ]);

  const instanceOpts = (instanceList || []).map((i) => ({ id: i.id, label: i.id }));
  const domainOpts = (stats.domains || []).map((d) => ({ id: d.name, label: d.name }));
  const endpointOpts = (stats.endpoints || []).map((e) => ({ id: e.route, label: e.route }));

  let rows = collectHintRows(stats);
  const summary = summarizeHints(rows);
  updateNavHintsBadge(summary);

  // filters
  if (selInstances !== null) {
    if (selInstances.length === 0) rows = [];
    else rows = rows.filter((r) => !r.instanceId || selInstances.includes(r.instanceId));
  }
  if (selDomains !== null) {
    if (selDomains.length === 0) rows = [];
    else rows = rows.filter((r) => !r.domain || selDomains.includes(r.domain));
  }
  if (selEndpoints !== null) {
    if (selEndpoints.length === 0) rows = [];
    else rows = rows.filter((r) => !r.route || selEndpoints.includes(r.route));
  }
  if (severity) {
    rows = rows.filter((r) => r.severity === severity);
  }

  // sort Critical > Warning > Info
  const rank = { Critical: 0, Warning: 1, Info: 2 };
  rows.sort((a, b) => (rank[a.severity] ?? 9) - (rank[b.severity] ?? 9) || a.code.localeCompare(b.code));

  main().innerHTML = `
    <div class="card">
      <h2>Hints ${severityStack(summary)}
        ${isHintMock() ? '<span class="badge">MOCK on</span>' : ""}
      </h2>
      <p class="muted">Rule-based recommendations. Filters combine (AND). Empty hint mark is <strong>○</strong>.</p>
      <form class="toolbar" id="hintFilters">
        ${multiSelectHtml("hInst", "Instances", instanceOpts, selInstances)}
        ${multiSelectHtml("hDom", "Domains", domainOpts, selDomains)}
        ${multiSelectHtml("hEp", "Endpoints", endpointOpts, selEndpoints)}
        <label>Severity
          <select name="severity">
            <option value="">All</option>
            ${["Critical","Warning","Info"].map(s =>
              `<option value="${s}" ${severity===s?"selected":""}>${s}</option>`).join("")}
          </select>
        </label>
        <button type="submit">Apply</button>
        <!-- REMOVE LATER — Hint mockup toggle -->
        <label class="toggle-inline" title="REMOVE LATER: UI preview only">
          <input type="checkbox" id="chkHintMock" ${isHintMock() ? "checked" : ""} />
          Mock hints (all pages)
        </label>
      </form>
      <div class="kpi-row">
        <div class="kpi"><div class="label">Critical</div><div class="value status-Down">${summary.critical}</div></div>
        <div class="kpi"><div class="label">Warning</div><div class="value" style="color:var(--warn)">${summary.warning}</div></div>
        <div class="kpi"><div class="label">Info</div><div class="value" style="color:var(--accent)">${summary.info}</div></div>
        <div class="kpi"><div class="label">Shown</div><div class="value">${rows.length}</div></div>
      </div>
      ${rows.length ? `
      <table class="dense entity-table hints-table">
        <thead>
          <tr>
            <th>Sev</th><th>Code</th><th>Message</th>
            <th>Instance</th><th>Domain</th><th>Endpoint</th><th>Entity</th>
          </tr>
        </thead>
        <tbody>
          ${rows.map(r => `
            <tr class="hint-table-row ${esc(r.severity)}">
              <td><span class="hint ${esc(r.severity)}">${esc(r.severity)}</span></td>
              <td><code>${esc(r.code)}</code></td>
              <td>${esc(r.message)}</td>
              <td>${r.instanceId ? `<a href="#/instances?id=${encodeURIComponent(r.instanceId)}"><code>${esc(r.instanceId)}</code></a>` : "—"}</td>
              <td>${r.domain ? `<a href="#/domains?name=${encodeURIComponent(r.domain)}"><code>${esc(r.domain)}</code></a>` : "—"}</td>
              <td>${r.route ? `<a href="#/endpoints?route=${encodeURIComponent(r.route)}"><code>${esc(r.route)}</code></a>` : "—"}</td>
              <td class="muted">${esc(r.entityType)}</td>
            </tr>`).join("")}
        </tbody>
      </table>` : `<p class="empty">No hints match filters${isHintMock() ? "" : " (enable Mock hints to preview symbology)"}.</p>`}
    </div>`;

  const form = $("#hintFilters");
  bindMultiSelects(form);
  form.addEventListener("submit", (ev) => {
    ev.preventDefault();
    const fd = new FormData(form);
    navigate("hints", {
      instances: csvParamFromSelection(readMultiSelect(form, "hInst")),
      domains: csvParamFromSelection(readMultiSelect(form, "hDom")),
      endpoints: csvParamFromSelection(readMultiSelect(form, "hEp")),
      severity: fd.get("severity") || "",
    });
  });
  $("#chkHintMock")?.addEventListener("change", (ev) => {
    setHintMock(ev.target.checked);
    refreshHeader();
    route();
  });
}

// —— F: Operations ——
async function renderOperations(params) {
  setBreadcrumb([{ label: "Operations" }]);
  const domain = params.get("domain") || "hello";
  const target = params.get("target") || "all";
  const action = params.get("action") || "invalidate";

  const instances = await api("/api/instances");

  main().innerHTML = `
    <div class="card">
      <h2>Operations</h2>
      <p class="muted">Fan-out writes to Local Admin APIs. Runtime version/TTL are process-local on each instance.</p>
      <form id="opForm" class="form-grid">
        <label>Action
          <select id="opAction" name="action">
            <option value="invalidate" ${action==="invalidate"?"selected":""}>Invalidate domain</option>
            <option value="entity" ${action==="entity"?"selected":""}>Invalidate entity</option>
            <option value="version" ${action==="version"?"selected":""}>Bump version</option>
            <option value="ttl" ${action==="ttl"?"selected":""}>Patch TTL</option>
          </select>
        </label>
        <label>Domain
          <input id="opDomain" name="domain" type="text" value="${esc(domain)}" required />
        </label>
        <label id="entityLabel" class="${action==="entity"?"":"hidden"}">Entity id
          <input id="opEntity" type="text" placeholder="resource id" />
        </label>
        <label>Target
          <select id="opTarget" name="target">
            <option value="all" ${target==="all"?"selected":""}>all</option>
            ${instances.map(i =>
              `<option value="instance:${esc(i.id)}" ${target===`instance:${i.id}`?"selected":""}>instance:${esc(i.id)}</option>`
            ).join("")}
          </select>
        </label>
        <label id="versionLabel" class="${action==="version"?"":"hidden"}">Version (optional)
          <input id="opVersion" type="text" placeholder="auto if empty" />
        </label>
        <label id="ttlLabel" class="${action==="ttl"?"":"hidden"}">OutputCacheTtlSeconds
          <input id="opTtl" type="number" min="0" value="120" />
        </label>
        <label id="ttlSoftLabel" class="${action==="ttl"?"":"hidden"}">Fusion soft TTL (optional)
          <input id="opTtlSoft" type="number" min="0" placeholder="leave empty" />
        </label>
        <button type="submit">Run</button>
      </form>
      <pre id="opResult" class="result">No operation yet.</pre>
    </div>
    <div class="card">
      <h2>Quick links</h2>
      <p class="muted">
        <a href="#/domains">Domains</a> ·
        <a href="#/instances">Instances</a>
      </p>
    </div>`;

  const actionEl = $("#opAction");
  function syncOpFields() {
    const a = actionEl.value;
    $("#entityLabel").classList.toggle("hidden", a !== "entity");
    $("#versionLabel").classList.toggle("hidden", a !== "version");
    $("#ttlLabel").classList.toggle("hidden", a !== "ttl");
    $("#ttlSoftLabel").classList.toggle("hidden", a !== "ttl");
  }
  actionEl.addEventListener("change", syncOpFields);

  $("#opForm").addEventListener("submit", async (ev) => {
    ev.preventDefault();
    const a = actionEl.value;
    const dom = $("#opDomain").value.trim();
    const tgt = $("#opTarget").value;
    const out = $("#opResult");
    out.textContent = "Running…";
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
        const body = {
          outputCacheTtlSeconds: Number($("#opTtl").value),
          target: tgt,
        };
        const soft = $("#opTtlSoft").value;
        if (soft !== "") body.fusionCacheSoftTtlSeconds = Number(soft);
        result = await api(`/api/domains/${encodeURIComponent(dom)}/ttl`, {
          method: "PATCH",
          body: JSON.stringify(body),
        });
      }
      out.textContent = JSON.stringify(result, null, 2);
      refreshHeader();
    } catch (err) {
      out.textContent = "Error: " + err.message;
    }
  });
}

// —— route dispatch ——
async function route() {
  const { path, params } = parseHash();
  const root = path.split("/")[0] || "overview";
  setNavActive(root);

  try {
    if (root === "overview" || path === "") {
      await renderOverview();
    } else if (root === "endpoints") {
      const routeName = params.get("route");
      if (routeName) await renderEndpointDetail(routeName);
      else await renderEndpointsList(params);
    } else if (root === "domains") {
      const name = params.get("name");
      if (name) await renderDomainDetail(name);
      else await renderDomainsList(params);
    } else if (root === "instances") {
      const id = params.get("id");
      if (id) await renderInstanceDetail(id);
      else await renderInstancesList();
    } else if (root === "operations") {
      await renderOperations(params);
    } else if (root === "hints" || root === "hints-mockup") {
      await renderHintsPage(params);
    } else {
      navigate("overview");
    }
  } catch (err) {
    console.error(err);
    main().innerHTML = `<div class="card"><p class="status-Down">${esc(err.message)}</p></div>`;
  }
}

function startHeaderRefresh() {
  refreshHeader();
  if (headerTimer) clearInterval(headerTimer);
  headerTimer = setInterval(refreshHeader, 15000);
}

$("#btnHeaderRefresh").addEventListener("click", () => {
  refreshHeader();
  route();
});

window.addEventListener("hashchange", route);

if (!location.hash) location.hash = "#/overview";
startHeaderRefresh();
route();
