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

function pct(rate) {
  if (rate == null || Number.isNaN(rate)) return "—";
  return (rate * 100).toFixed(1) + "%";
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

function renderStats(stats) {
  const el = document.getElementById("stats");
  document.getElementById("scopeLabel").textContent = stats.scope || "all";
  if (!stats.domains || !stats.domains.length) {
    el.innerHTML = `<p class="muted">No domain stats yet (instances down, or no traffic).</p>
      <p class="muted">Contributing instances: ${(stats.instances || []).map(i =>
        `${i.instanceId}:${i.succeeded ? "ok" : "fail"}`).join(", ") || "none"}</p>`;
    return;
  }
  el.innerHTML = `
    <table>
      <thead>
        <tr>
          <th>Domain</th><th>Version</th><th>OC hit%</th><th>FC hit%</th>
          <th>OC traffic</th><th>FC traffic</th><th>Invalidations</th>
        </tr>
      </thead>
      <tbody>
        ${stats.domains.map(d => `
          <tr>
            <td><code>${esc(d.name)}</code>${d.versionIsRuntimeOverride ? ' <span class="badge">rt</span>' : ""}</td>
            <td>${esc(d.version)}</td>
            <td>${pct(d.oc?.hitRate)}</td>
            <td>${pct(d.fc?.hitRate)}</td>
            <td>${(d.oc?.hits ?? 0) + (d.oc?.misses ?? 0)}</td>
            <td>${(d.fc?.hits ?? 0) + (d.fc?.misses ?? 0)}</td>
            <td>${d.invalidations ?? 0}</td>
          </tr>`).join("")}
      </tbody>
    </table>
    <p class="muted" style="margin-top:0.75rem">
      Instances: ${(stats.instances || []).map(i =>
        `<span class="status-${i.succeeded ? "Healthy" : "Down"}">${esc(i.instanceId)}</span>`).join(" · ")}
    </p>`;
}

function renderEndpoints(list) {
  const el = document.getElementById("endpoints");
  if (!list || !list.length) {
    el.innerHTML = `<p class="muted">No endpoint counters yet.</p>`;
    return;
  }
  el.innerHTML = `
    <table>
      <thead><tr><th>Route</th><th>Domain</th><th>OC hit%</th><th>FC hit%</th><th>FC miss%</th></tr></thead>
      <tbody>
        ${list.map(e => {
          const ft = (e.fc?.hits ?? 0) + (e.fc?.misses ?? 0);
          const miss = ft ? (e.fc.misses / ft) : null;
          return `<tr>
            <td><code>${esc(e.route)}</code></td>
            <td>${esc(e.configuredDomain || "—")}</td>
            <td>${pct(e.oc?.hitRate)}</td>
            <td>${pct(e.fc?.hitRate)}</td>
            <td>${pct(miss)}</td>
          </tr>`;
        }).join("")}
      </tbody>
    </table>`;
}

function esc(s) {
  return String(s ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

async function refresh() {
  try {
    const [instances, stats, endpoints] = await Promise.all([
      api("/api/instances"),
      api("/api/stats?scope=all"),
      api("/api/endpoints?sort=missRate&take=15"),
    ]);
    renderInstances(instances);
    renderStats(stats);
    renderEndpoints(endpoints);
  } catch (err) {
    console.error(err);
    document.getElementById("instances").innerHTML =
      `<p class="status-Down">Failed to load: ${esc(err.message)}</p>`;
  }
}

document.getElementById("btnRefresh").addEventListener("click", refresh);

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
