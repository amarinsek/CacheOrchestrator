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

function pct(rate, lowSample) {
  if (rate == null || Number.isNaN(rate)) return "—";
  const s = (rate * 100).toFixed(1) + "%";
  return lowSample ? `<span class="low-n" title="Low sample size">${s}</span>` : s;
}

function pipelineBar(p) {
  if (!p) return "";
  const parts = [
    ["oc", p.ocHitShare, "OC hit"],
    ["fc", p.fcHitShare, "FC hit"],
    ["origin", p.originShare, "Origin"],
    ["bypass", p.bypassShare, "Bypass"],
    ["other", p.otherShare, "Other"],
  ].filter(([, v]) => v != null && v > 0.0005);
  if (!parts.length) return `<div class="pipe empty"></div>`;
  return `<div class="pipe" title="${parts.map(([,,l,]) => l).join(" · ")}">${
    parts.map(([cls, v, label]) =>
      `<span class="seg ${cls}" style="flex:${Math.max(v, 0.01)}" title="${label}: ${(v*100).toFixed(1)}%"></span>`
    ).join("")
  }</div>`;
}

function spreadCell(s) {
  if (!s || s.sampleCount < 1) return "—";
  if (s.sampleCount === 1) return pct(s.mean);
  return `${pct(s.min)}–${pct(s.max)} <span class="muted">(μ ${pct(s.mean)})</span>`;
}

function esc(s) {
  return String(s ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

function renderInstances(list) {
  const el = document.getElementById("instances");
  const target = document.getElementById("opTarget");
  target.innerHTML = `<option value="all">all</option>`;
  if (!list || !list.length) {
    el.innerHTML = `<p class="muted">No instances configured. Edit <code>CacheAdmin:Instances</code>.</p>`;
    return;
  }
  for (const i of list) {
    const opt = document.createElement("option");
    opt.value = `instance:${i.id}`;
    opt.textContent = `instance:${i.id}`;
    target.appendChild(opt);
  }
  el.innerHTML = `
    <table>
      <thead><tr><th>Id</th><th>URL</th><th>Status</th><th>Reported</th><th>Latency</th><th>Error</th></tr></thead>
      <tbody>
        ${list.map(i => `
          <tr>
            <td><code>${esc(i.id)}</code></td>
            <td><code>${esc(i.url)}</code></td>
            <td class="status-${esc(i.status)}">${esc(i.status)}</td>
            <td>${esc(i.reportedInstanceId || "—")}</td>
            <td>${i.latencyMs != null ? Math.round(i.latencyMs) + " ms" : "—"}</td>
            <td class="muted">${esc(i.error || "")}</td>
          </tr>`).join("")}
      </tbody>
    </table>`;
}

function endpointRows(list, groupBy) {
  const rows = [];
  for (const e of list) {
    rows.push(endpointRow(e, false));
    if (groupBy && e.byInstance && e.byInstance.length) {
      for (const bi of e.byInstance) rows.push(endpointRow(bi, true));
    }
  }
  return rows.join("");
}

function endpointRow(e, child) {
  const cls = child ? "child" : "";
  const route = child
    ? `<span class="indent">↳</span> <code class="muted">${esc(e.instanceId || "?")}</code>`
    : `<code>${esc(e.route)}</code>`;
  return `<tr class="${cls}">
    <td>${route}</td>
    <td>${esc(e.configuredDomain || "—")}</td>
    <td>${e.requests ?? 0}</td>
    <td>${pipelineBar(e.pipeline)}</td>
    <td>${pct(e.oc?.hitShare, e.oc?.lowSample)}</td>
    <td>${pct(e.fc?.hitShare, e.fc?.lowSample)}</td>
    <td>${pct(e.fc?.missShare, e.fc?.lowSample)}</td>
    <td>${pct(e.fc?.originShare, e.fc?.lowSample)}</td>
    <td class="secondary">${pct(e.oc?.hitRate, e.oc?.lowSample)}</td>
    <td class="secondary">${pct(e.fc?.hitRate, e.fc?.lowSample)}</td>
    <td class="secondary">${pct(e.fc?.missRate, e.fc?.lowSample)}</td>
    ${!child && e.instanceSpread ? `<td class="muted">${spreadCell(e.instanceSpread.ocHitShare)}</td>` : (child ? "" : "<td>—</td>")}
  </tr>`;
}

function renderEndpoints(list, groupBy) {
  const el = document.getElementById("endpoints");
  if (!list || !list.length) {
    el.innerHTML = `<p class="muted">No endpoint counters yet. Generate traffic on instances with Admin enabled.</p>`;
    return;
  }
  el.innerHTML = `
    <table class="dense">
      <thead>
        <tr>
          <th>Route / instance</th><th>Domain</th><th>Req</th><th>Pipeline</th>
          <th>OC hit share</th><th>FC hit share</th><th>FC miss share</th><th>Origin share</th>
          <th class="secondary">OC hit rate</th><th class="secondary">FC hit rate</th><th class="secondary">FC miss rate</th>
          <th>OC hit share σ</th>
        </tr>
      </thead>
      <tbody>${endpointRows(list, groupBy)}</tbody>
    </table>`;
}

function domainRows(list, groupBy) {
  const rows = [];
  for (const d of list) {
    rows.push(domainRow(d, false));
    if (groupBy && d.byInstance && d.byInstance.length) {
      for (const bi of d.byInstance) rows.push(domainRow(bi, true));
    }
  }
  return rows.join("");
}

function domainRow(d, child) {
  const name = child
    ? `<span class="indent">↳</span> <code class="muted">${esc(d.instanceId || "?")}</code>`
    : `<code>${esc(d.name)}</code>${d.versionIsRuntimeOverride ? ' <span class="badge">rt</span>' : ""}`;
  return `<tr class="${child ? "child" : ""}">
    <td>${name}</td>
    <td>${esc(d.version)}</td>
    <td>${d.requests ?? 0}</td>
    <td>${pipelineBar(d.pipeline)}</td>
    <td>${pct(d.oc?.hitShare, d.oc?.lowSample)}</td>
    <td>${pct(d.fc?.hitShare, d.fc?.lowSample)}</td>
    <td>${pct(d.fc?.originShare, d.fc?.lowSample)}</td>
    <td class="secondary">${pct(d.oc?.hitRate, d.oc?.lowSample)}</td>
    <td class="secondary">${pct(d.fc?.hitRate, d.fc?.lowSample)}</td>
    <td>${d.invalidations ?? 0}</td>
    ${!child && d.instanceSpread ? `<td class="muted">${spreadCell(d.instanceSpread.fcHitShare)}</td>` : (child ? "" : "<td>—</td>")}
  </tr>`;
}

function renderDomains(list, groupBy) {
  const el = document.getElementById("domains");
  if (!list || !list.length) {
    el.innerHTML = `<p class="muted">No domain stats yet.</p>`;
    return;
  }
  el.innerHTML = `
    <table class="dense">
      <thead>
        <tr>
          <th>Domain / instance</th><th>Version</th><th>Req</th><th>Pipeline</th>
          <th>OC hit share</th><th>FC hit share</th><th>Origin share</th>
          <th class="secondary">OC hit rate</th><th class="secondary">FC hit rate</th>
          <th>Invalidations</th><th>FC hit share range</th>
        </tr>
      </thead>
      <tbody>${domainRows(list, groupBy)}</tbody>
    </table>`;
}

async function refresh() {
  const groupBy = document.getElementById("chkGroupByInstance").checked;
  const q = groupBy ? "&groupByInstance=true" : "";
  try {
    const [instances, stats, endpoints] = await Promise.all([
      api("/api/instances"),
      api(`/api/stats?scope=all${q}`),
      api(`/api/endpoints?sort=requests&take=50${q}`),
    ]);
    document.getElementById("scopeLabel").textContent = stats.scope || "all";
    renderInstances(instances);
    renderEndpoints(endpoints, groupBy);
    renderDomains(stats.domains || [], groupBy);
  } catch (err) {
    console.error(err);
    document.getElementById("instances").innerHTML =
      `<p class="status-Down">Failed to load: ${esc(err.message)}</p>`;
  }
}

document.getElementById("btnRefresh").addEventListener("click", refresh);
document.getElementById("chkGroupByInstance").addEventListener("change", refresh);

const actionEl = document.getElementById("opAction");
function syncOpFields() {
  const a = actionEl.value;
  document.getElementById("ttlLabel").classList.toggle("hidden", a !== "ttl");
  document.getElementById("versionLabel").classList.toggle("hidden", a !== "version");
}
actionEl.addEventListener("change", syncOpFields);
syncOpFields();

document.getElementById("opForm").addEventListener("submit", async (ev) => {
  ev.preventDefault();
  const action = actionEl.value;
  const domain = document.getElementById("opDomain").value.trim();
  const target = document.getElementById("opTarget").value;
  const out = document.getElementById("opResult");
  out.textContent = "Running…";
  try {
    let result;
    if (action === "invalidate") {
      result = await api("/api/invalidate", {
        method: "POST",
        body: JSON.stringify({ scope: "domain", domain, target }),
      });
    } else if (action === "version") {
      const version = document.getElementById("opVersion").value.trim();
      result = await api(`/api/domains/${encodeURIComponent(domain)}/version`, {
        method: "POST",
        body: JSON.stringify({ version: version || null, target }),
      });
    } else {
      const ttl = Number(document.getElementById("opTtl").value);
      result = await api(`/api/domains/${encodeURIComponent(domain)}/ttl`, {
        method: "PATCH",
        body: JSON.stringify({ outputCacheTtlSeconds: ttl, target }),
      });
    }
    out.textContent = JSON.stringify(result, null, 2);
    await refresh();
  } catch (err) {
    out.textContent = "Error: " + err.message;
  }
});

refresh();
