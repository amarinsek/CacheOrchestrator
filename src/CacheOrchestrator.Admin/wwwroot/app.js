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

/** Rule-based hint badges (recommendations). */
function hintBadges(hints) {
  if (!hints || !hints.length) return "";
  return `<span class="hint-badges">${hints.map(h =>
    `<span class="hint ${esc(h.severity || "Info")}" title="${esc(h.message)}">${esc(shortHint(h))}</span>`
  ).join("")}</span>`;
}

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

function parseCsvParam(params, key) {
  const raw = params.get(key) || "";
  if (!raw || raw === "*") return [];
  return raw.split(",").map((s) => s.trim()).filter(Boolean);
}

function csvParam(ids) {
  return ids && ids.length ? ids.join(",") : "";
}

/** Multi-select dropdown: options [{id,label}], selectedIds array. empty = all. */
function multiSelectHtml(id, label, options, selectedIds) {
  const all = !selectedIds || selectedIds.length === 0;
  const summary = all
    ? "All"
    : selectedIds.length <= 2
      ? selectedIds.join(", ")
      : `${selectedIds.length} selected`;
  return `
    <label class="ms" data-ms="${esc(id)}">
      <span>${esc(label)}</span>
      <button type="button" class="ms-btn" data-ms-toggle="${esc(id)}">${esc(summary)} ▾</button>
      <div class="ms-panel hidden" data-ms-panel="${esc(id)}">
        <div class="ms-actions">
          <button type="button" class="secondary" data-ms-all="${esc(id)}">All</button>
          <button type="button" class="secondary" data-ms-none="${esc(id)}">None</button>
        </div>
        ${options.map((o) => {
          const checked = all || selectedIds.includes(o.id);
          return `<label><input type="checkbox" value="${esc(o.id)}" ${checked ? "checked" : ""}/> ${esc(o.label)}</label>`;
        }).join("")}
      </div>
    </label>`;
}

function bindMultiSelects(root, onChange) {
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
      root.querySelectorAll(`[data-ms-panel="${id}"] input[type=checkbox]`).forEach((c) => { c.checked = true; });
      updateMsSummary(root, id);
      onChange?.();
    });
  });
  root.querySelectorAll("[data-ms-none]").forEach((btn) => {
    btn.addEventListener("click", (ev) => {
      ev.preventDefault();
      const id = btn.dataset.msNone;
      root.querySelectorAll(`[data-ms-panel="${id}"] input[type=checkbox]`).forEach((c) => { c.checked = false; });
      updateMsSummary(root, id);
      onChange?.();
    });
  });
  root.querySelectorAll("[data-ms-panel] input[type=checkbox]").forEach((cb) => {
    cb.addEventListener("change", () => {
      const panel = cb.closest("[data-ms-panel]");
      const id = panel?.dataset.msPanel;
      if (id) updateMsSummary(root, id);
      onChange?.();
    });
  });
  document.addEventListener("click", closeMsOutside, { once: false });
}

function closeMsOutside(ev) {
  if (ev.target.closest("[data-ms]")) return;
  document.querySelectorAll("[data-ms-panel]").forEach((p) => p.classList.add("hidden"));
}

function updateMsSummary(root, id) {
  const panel = root.querySelector(`[data-ms-panel="${id}"]`);
  const btn = root.querySelector(`[data-ms-toggle="${id}"]`);
  if (!panel || !btn) return;
  const boxes = [...panel.querySelectorAll("input[type=checkbox]")];
  const checked = boxes.filter((c) => c.checked).map((c) => c.value);
  const all = checked.length === 0 || checked.length === boxes.length;
  btn.textContent = (all ? "All" : checked.length <= 2 ? checked.join(", ") : `${checked.length} selected`) + " ▾";
}

/** Selected ids; empty array means all (no filter). */
function readMultiSelect(root, id) {
  const panel = root.querySelector(`[data-ms-panel="${id}"]`);
  if (!panel) return [];
  const boxes = [...panel.querySelectorAll("input[type=checkbox]")];
  const checked = boxes.filter((c) => c.checked).map((c) => c.value);
  if (checked.length === 0 || checked.length === boxes.length) return [];
  return checked;
}

function hintListHtml(hints) {
  if (!hints || !hints.length) return "";
  return `<ul class="alert-list" style="margin-top:0.75rem">${hints.map(h =>
    `<li><strong class="hint ${esc(h.severity)}">${esc(h.severity)}</strong> ${esc(h.message)}</li>`
  ).join("")}</ul>`;
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
    const o = await api("/api/overview");
    lastOverview = o;
    renderHeader(o);
  } catch (err) {
    $("#headerMetrics").innerHTML =
      `<span class="hm status-Down">Header: ${esc(err.message)}</span>`;
  }
}

function renderHeader(o) {
  const healthDots = [
    ...Array(o.healthyCount || 0).fill("ok"),
    ...Array(o.degradedCount || 0).fill("warn"),
    ...Array(o.downCount || 0).fill("bad"),
  ].map((c) => `<span class="dot ${c}"></span>`).join("") || `<span class="muted">no instances</span>`;

  $("#headerMetrics").innerHTML = `
    <span class="hm" title="Instance health">${healthDots}
      <strong>${o.healthyCount ?? 0}</strong><span class="muted">up</span>
      ${(o.downCount || 0) > 0 ? `<span class="status-Down">${o.downCount} down</span>` : ""}
    </span>
    <span class="hm" title="Request pipeline">${pipelineBar(o.pipeline)}</span>
    <span class="hm">OC hit <strong>${pct(o.ocHitShare)}</strong></span>
    <span class="hm">Origin <strong>${pct(o.originShare)}</strong></span>
    <span class="hm">Req <strong>${num(o.totalRequests)}</strong></span>
    <span class="hm">Inv <strong>${num(o.totalInvalidations)}</strong></span>
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
  const o = await api("/api/overview");
  lastOverview = o;
  renderHeader(o);

  main().innerHTML = `
    <div class="kpi-row">
      <div class="kpi"><div class="label">Instances up</div><div class="value status-Healthy">${o.healthyCount}/${(o.instances||[]).length}</div></div>
      <div class="kpi"><div class="label">Requests</div><div class="value">${num(o.totalRequests)}</div></div>
      <div class="kpi"><div class="label">OC hit share</div><div class="value">${pct(o.ocHitShare)}</div></div>
      <div class="kpi"><div class="label">Origin share</div><div class="value">${pct(o.originShare)}</div></div>
      <div class="kpi"><div class="label">Domains</div><div class="value">${num(o.domainCount)}</div></div>
      <div class="kpi"><div class="label">Endpoints</div><div class="value">${num(o.endpointCount)}</div></div>
    </div>
    <div class="card">
      <h2>Cluster pipeline</h2>
      ${pipelineBar(o.pipeline, true)}
      <p class="muted" style="margin:0.5rem 0 0;font-size:0.85rem">OC hit · FC hit · Origin · Bypass — shares of total requests</p>
    </div>
    ${o.alerts?.length ? `<div class="card"><h2>Alerts</h2><ul class="alert-list">${o.alerts.map(a => `<li>${esc(a)}</li>`).join("")}</ul></div>` : ""}
    <div class="grid-2">
      <div class="card">
        <h2>Instances</h2>
        <table class="dense">
          <thead><tr><th>Id</th><th>Status</th><th>Latency</th></tr></thead>
          <tbody>
            ${(o.instances || []).map(i => `
              <tr class="clickable" data-go="instances" data-id="${esc(i.id)}">
                <td><code>${esc(i.id)}</code></td>
                <td class="status-${esc(i.status)}">${esc(i.status)}</td>
                <td>${i.latencyMs != null ? Math.round(i.latencyMs) + " ms" : "—"}</td>
              </tr>`).join("") || `<tr><td colspan="3" class="empty">None configured</td></tr>`}
          </tbody>
        </table>
      </div>
      <div class="card">
        <h2>Top endpoints <span class="badge">origin / traffic</span></h2>
        <table class="dense">
          <thead><tr><th>Route</th><th>Req</th><th>Origin</th><th>OC hit</th></tr></thead>
          <tbody>
            ${(o.topEndpoints || []).map(e => `
              <tr class="clickable" data-go="endpoints" data-route="${esc(e.route)}">
                <td><code>${esc(e.route)}</code> ${hintBadges(e.hints)}</td>
                <td>${num(e.requests)}</td>
                <td>${pct(e.fc?.originShare, e.fc?.lowSample)}</td>
                <td>${pct(e.oc?.hitShare, e.oc?.lowSample)}</td>
              </tr>`).join("") || `<tr><td colspan="4" class="empty">No traffic yet</td></tr>`}
          </tbody>
        </table>
        <p style="margin:0.75rem 0 0"><a href="#/endpoints">All endpoints →</a></p>
      </div>
    </div>`;

  main().querySelectorAll("[data-go]").forEach((tr) => {
    tr.addEventListener("click", () => {
      if (tr.dataset.go === "instances") navigate("instances", { id: tr.dataset.id });
      if (tr.dataset.go === "endpoints") navigate("endpoints", { route: tr.dataset.route });
    });
  });
}

// —— C: Endpoints list + detail ——
async function renderEndpointsList(params) {
  setBreadcrumb([{ label: "Endpoints", href: "#/endpoints" }]);
  const search = params.get("search") || "";
  const sort = params.get("sort") || "requests";
  const minRequests = params.get("minRequests") || "0";
  const take = params.get("take") || "50";
  const skip = Number(params.get("skip") || "0");
  let selInstances = parseCsvParam(params, "instances");
  let selDomains = parseCsvParam(params, "domains");

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
      instances: csvParam(readMultiSelect(form, "epInst")),
      domains: csvParam(readMultiSelect(form, "epDom")),
      minRequests: fd.get("minRequests"),
      sort: fd.get("sort"),
      take,
      skip: 0,
    });
  });

  // re-read selection from URL after render
  selInstances = parseCsvParam(params, "instances");
  selDomains = parseCsvParam(params, "domains");

  const q = new URLSearchParams({ sort, take, skip: String(skip), search, minRequests });
  if (selInstances.length) q.set("instances", selInstances.join(","));
  if (selDomains.length) q.set("domains", selDomains.join(","));
  const list = await api("/api/endpoints?" + q.toString());

  $("#epTable").innerHTML = `
    <table class="dense">
      <thead>
        <tr>
          <th>Route</th><th>Domain</th><th>Hints</th><th>Req</th><th>Pipeline</th>
          <th>OC hit share</th><th>Origin share</th>
          <th class="secondary">FC miss rate</th>
        </tr>
      </thead>
      <tbody>
        ${list.length ? list.map(e => `
          <tr class="clickable" data-route="${esc(e.route)}">
            <td><code>${esc(e.route)}</code></td>
            <td>${e.configuredDomain ? `<a href="#/domains?name=${encodeURIComponent(e.configuredDomain)}">${esc(e.configuredDomain)}</a>` : "—"}</td>
            <td>${hintBadges(e.hints)}</td>
            <td>${num(e.requests)}</td>
            <td>${pipelineBar(e.pipeline)}</td>
            <td>${pct(e.oc?.hitShare, e.oc?.lowSample)}</td>
            <td>${pct(e.fc?.originShare, e.fc?.lowSample)}</td>
            <td class="secondary">${pct(e.fc?.missRate, e.fc?.lowSample)}</td>
          </tr>`).join("") : `<tr><td colspan="8" class="empty">No endpoints match filters</td></tr>`}
      </tbody>
    </table>
    <div class="pager">
      <button type="button" class="secondary" id="epPrev" ${skip<=0?"disabled":""}>Prev</button>
      <span>skip ${skip}</span>
      <button type="button" class="secondary" id="epNext" ${list.length < Number(take)?"disabled":""}>Next</button>
    </div>`;

  $("#epTable").querySelectorAll("tr.clickable").forEach((tr) => {
    tr.addEventListener("click", (ev) => {
      if (ev.target.closest("a")) return;
      navigate("endpoints", { route: tr.dataset.route });
    });
  });
  const pageParams = () => ({
    search, sort, minRequests, take,
    instances: csvParam(selInstances),
    domains: csvParam(selDomains),
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
  const ep = (stats.endpoints || []).find((e) => e.route === route);
  if (!ep) {
    main().innerHTML = `<div class="card"><p class="status-Down">Endpoint not found: <code>${esc(route)}</code></p>
      <a href="#/endpoints">← Back</a></div>`;
    return;
  }

  main().innerHTML = `
    <div class="card">
      <h2><code>${esc(ep.route)}</code>
        ${ep.configuredDomain ? `<a class="badge" href="#/domains?name=${encodeURIComponent(ep.configuredDomain)}">${esc(ep.configuredDomain)}</a>` : ""}
        ${hintBadges(ep.hints)}
      </h2>
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
  const selInstances = parseCsvParam(params, "instances");

  main().innerHTML = `<div class="card"><p class="muted">Loading domains…</p></div>`;
  const instanceList = await api("/api/instances");
  const instanceOpts = (instanceList || []).map((i) => ({ id: i.id, label: i.id }));

  const q = new URLSearchParams({ scope: "all" });
  if (selInstances.length) q.set("instances", selInstances.join(","));
  const stats = await api("/api/stats?" + q.toString());
  const domains = stats.domains || [];

  main().innerHTML = `
    <div class="card">
      <h2>Domains</h2>
      <form class="toolbar" id="domFilters">
        ${multiSelectHtml("domInst", "Instances", instanceOpts, selInstances)}
        <button type="submit">Apply</button>
      </form>
      <table class="dense">
        <thead>
          <tr>
            <th>Domain</th><th>Hints</th><th>Version</th><th>Req</th><th>Pipeline</th>
            <th>OC hit share</th><th>Origin</th><th>Invalidations</th><th></th>
          </tr>
        </thead>
        <tbody>
          ${domains.map(d => `
            <tr class="clickable" data-name="${esc(d.name)}">
              <td><code>${esc(d.name)}</code>${d.versionIsRuntimeOverride ? ' <span class="badge">rt</span>' : ""}</td>
              <td>${hintBadges(d.hints)}</td>
              <td>${esc(d.version)}</td>
              <td>${num(d.requests)}</td>
              <td>${pipelineBar(d.pipeline)}</td>
              <td>${pct(d.oc?.hitShare, d.oc?.lowSample)}</td>
              <td>${pct(d.fc?.originShare)}</td>
              <td>${num(d.invalidations)}</td>
              <td><a href="#/operations?domain=${encodeURIComponent(d.name)}" onclick="event.stopPropagation()">Ops</a></td>
            </tr>`).join("") || `<tr><td colspan="9" class="empty">No domains</td></tr>`}
        </tbody>
      </table>
    </div>`;

  const form = $("#domFilters");
  bindMultiSelects(form);
  form.addEventListener("submit", (ev) => {
    ev.preventDefault();
    navigate("domains", { instances: csvParam(readMultiSelect(form, "domInst")) });
  });

  main().querySelectorAll("tr.clickable").forEach((tr) => {
    tr.addEventListener("click", () => navigate("domains", {
      name: tr.dataset.name,
      instances: csvParam(selInstances),
    }));
  });
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
  const d = (stats.domains || []).find((x) => x.name === name);
  const cfg = (cfgFan.data || []).find((x) => x.name === name);

  if (!d && !cfg) {
    main().innerHTML = `<div class="card"><p class="status-Down">Domain not found</p></div>`;
    return;
  }

  const domain = d || { name, requests: 0, oc: {}, fc: {}, pipeline: {}, endpoints: [] };

  main().innerHTML = `
    <div class="card">
      <h2><code>${esc(name)}</code>
        ${domain.versionIsRuntimeOverride ? '<span class="badge">runtime version</span>' : ""}
        ${hintBadges(domain.hints)}
        <a class="badge" href="#/operations?domain=${encodeURIComponent(name)}">Operations</a>
      </h2>
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
      <table class="dense">
        <thead><tr><th>Route</th><th>Req</th><th>OC hit share</th><th>Origin</th><th>Stale</th></tr></thead>
        <tbody>
          ${(domain.endpoints || []).map(e => `
            <tr class="clickable" data-route="${esc(e.route)}">
              <td><code>${esc(e.route)}</code></td>
              <td>${num(e.requests)}</td>
              <td>${pct(e.oc?.hitShare, e.oc?.lowSample)}</td>
              <td>${pct(e.fc?.originShare)}</td>
              <td>${num(e.fc?.stale)}</td>
            </tr>`).join("") || `<tr><td colspan="5" class="empty">No endpoints</td></tr>`}
        </tbody>
      </table>
    </div>
    <p><a href="#/domains">← Domains</a> · <a href="#/operations?domain=${encodeURIComponent(name)}">Operations</a></p>`;

  main().querySelectorAll("tr.clickable[data-id]").forEach((tr) => {
    tr.addEventListener("click", () => navigate("instances", { id: tr.dataset.id }));
  });
  main().querySelectorAll("tr.clickable[data-route]").forEach((tr) => {
    tr.addEventListener("click", () => navigate("endpoints", { route: tr.dataset.route }));
  });
}

// —— E: Instances ——
async function renderInstancesList() {
  setBreadcrumb([{ label: "Instances", href: "#/instances" }]);
  main().innerHTML = `<p class="muted">Loading…</p>`;
  const [list, overview] = await Promise.all([
    api("/api/instances"),
    api("/api/overview"),
  ]);
  renderHeader(overview);

  main().innerHTML = `
    <div class="card">
      <h2>Instances</h2>
      <table>
        <thead><tr><th>Id</th><th>URL</th><th>Status</th><th>Reported</th><th>Latency</th><th>Error</th></tr></thead>
        <tbody>
          ${list.map(i => `
            <tr class="clickable" data-id="${esc(i.id)}">
              <td><code>${esc(i.id)}</code></td>
              <td><code>${esc(i.url)}</code></td>
              <td class="status-${esc(i.status)}">${esc(i.status)}</td>
              <td>${esc(i.reportedInstanceId || "—")}</td>
              <td>${i.latencyMs != null ? Math.round(i.latencyMs) + " ms" : "—"}</td>
              <td class="muted">${esc(i.error || "")}</td>
            </tr>`).join("") || `<tr><td colspan="6" class="empty">None configured</td></tr>`}
        </tbody>
      </table>
    </div>`;

  main().querySelectorAll("tr.clickable").forEach((tr) => {
    tr.addEventListener("click", () => navigate("instances", { id: tr.dataset.id }));
  });
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

  main().innerHTML = `
    <div class="card">
      <h2>Instance <code>${esc(id)}</code>
        <span class="status-${esc(inst?.status || "Down")}">${esc(inst?.status || "unknown")}</span>
      </h2>
      <p class="muted"><code>${esc(inst?.url || "")}</code>
        · reported <code>${esc(inst?.reportedInstanceId || "—")}</code>
        · ${inst?.latencyMs != null ? Math.round(inst.latencyMs) + " ms" : "—"}
        ${inst?.error ? ` · <span class="status-Down">${esc(inst.error)}</span>` : ""}
      </p>
      <div class="kpi-row">
        <div class="kpi"><div class="label">Domains</div><div class="value">${(stats.domains||[]).length}</div></div>
        <div class="kpi"><div class="label">Endpoints</div><div class="value">${(stats.endpoints||[]).length}</div></div>
        <div class="kpi"><div class="label">Requests</div><div class="value">${num((stats.domains||[]).reduce((s,d)=>s+(d.requests||0),0))}</div></div>
      </div>
    </div>
    <div class="card">
      <h2>Domains on instance</h2>
      <table class="dense">
        <thead><tr><th>Domain</th><th>Version</th><th>Req</th><th>OC hit share</th><th>Origin</th><th>Stale</th><th>Factory</th></tr></thead>
        <tbody>
          ${(stats.domains||[]).map(d => `
            <tr class="clickable" data-name="${esc(d.name)}">
              <td><code>${esc(d.name)}</code></td>
              <td>${esc(d.version)}</td>
              <td>${num(d.requests)}</td>
              <td>${pct(d.oc?.hitShare, d.oc?.lowSample)}</td>
              <td>${pct(d.fc?.originShare)}</td>
              <td>${num(d.fc?.stale)}</td>
              <td>${num(d.fc?.factoryRuns)} / fail ${num(d.fc?.factoryFailures)}</td>
            </tr>`).join("") || `<tr><td colspan="7" class="empty">No data</td></tr>`}
        </tbody>
      </table>
    </div>
    <div class="card">
      <h2>Endpoints on instance</h2>
      <table class="dense">
        <thead><tr><th>Route</th><th>Domain</th><th>Req</th><th>Pipeline</th><th>Origin</th></tr></thead>
        <tbody>
          ${(stats.endpoints||[]).slice(0, 50).map(e => `
            <tr class="clickable" data-route="${esc(e.route)}">
              <td><code>${esc(e.route)}</code></td>
              <td>${esc(e.configuredDomain || "—")}</td>
              <td>${num(e.requests)}</td>
              <td>${pipelineBar(e.pipeline)}</td>
              <td>${pct(e.fc?.originShare)}</td>
            </tr>`).join("") || `<tr><td colspan="5" class="empty">No data</td></tr>`}
        </tbody>
      </table>
    </div>
    <p><a href="#/instances">← Instances</a>
      · <a href="#/operations?target=instance:${encodeURIComponent(id)}">Operations on this instance</a></p>`;

  main().querySelectorAll("tr.clickable[data-name]").forEach((tr) => {
    tr.addEventListener("click", () => navigate("domains", { name: tr.dataset.name }));
  });
  main().querySelectorAll("tr.clickable[data-route]").forEach((tr) => {
    tr.addEventListener("click", () => navigate("endpoints", { route: tr.dataset.route }));
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
