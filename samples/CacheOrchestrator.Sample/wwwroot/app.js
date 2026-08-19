import { EditorView, basicSetup } from 'codemirror';
import { json } from '@codemirror/lang-json';
import { oneDark } from '@codemirror/theme-one-dark';

// ─── DOM refs ───────────────────────────────────────────────────────────────
const logEl           = document.getElementById('log');
const endpointEl      = document.getElementById('endpoint');
const domainNameEl    = document.getElementById('domainName');
const editorOverlay   = document.getElementById('editorOverlay');
const editorMount     = document.getElementById('editorMount');
const editorErrorEl   = document.getElementById('editorError');
const logHintEl       = document.getElementById('logHint');
const btnOnceEl       = document.getElementById('btnOnce');
const btnTwiceEl      = document.getElementById('btnTwice');
const panelDomain     = document.getElementById('panelDomain');
const panelCrud       = document.getElementById('panelCrud');
const crudBackendEl   = document.getElementById('crudBackend');

const CRUD_URL = '/api/crud/products/42';
const CRUD_DOMAIN = 'product-crud';

let endpoints = [];

// ─── CodeMirror editor instance ─────────────────────────────────────────────
let cmEditor = null;

function initEditor(content) {
    if (cmEditor) {
        cmEditor.destroy();
        cmEditor = null;
    }
    editorMount.innerHTML = '';
    cmEditor = new EditorView({
        doc: content,
        extensions: [basicSetup, json(), oneDark],
        parent: editorMount
    });
}

function getEditorContent() {
    return cmEditor ? cmEditor.state.doc.toString() : '';
}

function selectedEndpoint() {
    const key = endpointEl.value;
    return endpoints.find(x => optionKey(x) === key) ?? null;
}

function optionKey(e) {
    return `${(e.method || 'GET').toUpperCase()} ${e.url}`;
}

async function loadEndpoints() {
    const all = await (await fetch('/api/demo/endpoints', { cache: 'no-store' })).json();
    // Domain panel: config-driven routes only (CRUD lives in its own panel).
    endpoints = all.filter(e => e.source !== 'hardcoded');
    endpointEl.innerHTML = endpoints.map(e => {
        const method = (e.method || 'GET').toUpperCase();
        const label = e.label || e.url;
        return `<option value="${escAttr(optionKey(e))}">${escHtml(method)} ${escHtml(label)} (${escHtml(e.domain)})</option>`;
    }).join('');
    updateDomainLabel();

    const crud = all.find(e => e.source === 'hardcoded' && e.domain === CRUD_DOMAIN)
        || all.find(e => (e.url || '').includes('/api/crud/products'));
    if (crudBackendEl) {
        crudBackendEl.textContent = crud?.backend || '…';
    }
}

function updateDomainLabel() {
    const e = selectedEndpoint();
    domainNameEl.textContent = e?.domain ?? '—';
    document.getElementById('domainBackend').textContent = e?.backend ?? '…';
}

// ─── Panel switch ────────────────────────────────────────────────────────────
function setPanel(name) {
    const isDomain = name === 'domain';
    panelDomain.classList.toggle('hidden', !isDomain);
    panelDomain.hidden = !isDomain;
    panelCrud.classList.toggle('hidden', isDomain);
    panelCrud.hidden = isDomain;

    document.querySelectorAll('.panel-tab').forEach((tab) => {
        const on = tab.dataset.panel === name;
        tab.classList.toggle('active', on);
        tab.setAttribute('aria-selected', on ? 'true' : 'false');
    });
}

document.querySelectorAll('.panel-tab').forEach((tab) => {
    tab.addEventListener('click', () => setPanel(tab.dataset.panel));
});

// ─── Fetch helpers ───────────────────────────────────────────────────────────
function pickDomainUrl() {
    const e = selectedEndpoint();
    let url = e?.url || endpointEl.value;
    url = url.replace(/{id}/g, '42').replace(/{[^}]+}/g, 'demo');
    const extra = document.getElementById('extraQuery').value.trim();
    if (extra) url += (url.includes('?') ? '&' : '?') + extra.replace(/^\?/, '');
    return url;
}

/**
 * Fetch options for playground requests.
 * Default cache: 'no-store' (server OC/FC visible). Checkbox uses cache: 'default' for client max-age demos.
 */
/**
 * @param {'GET'|'PUT'} [method]
 * @param {{ disableBrowserCache?: boolean, price?: number }} [opts]
 */
function buildFetchInit(method = 'GET', { disableBrowserCache = true, price } = {}) {
    /** @type {RequestInit} */
    const init = {
        method,
        // Fetch cache mode only — not HTTP Cache-Control: no-store (server OC/FC still run).
        cache: disableBrowserCache ? 'no-store' : 'default',
        headers: {},
    };
    const acceptEl = document.getElementById('acceptHeader');
    if (acceptEl?.value) {
        init.headers = { Accept: acceptEl.value, ...init.headers };
    }
    if (method === 'PUT') {
        init.headers = { 'Content-Type': 'application/json', ...init.headers };
        init.body = JSON.stringify({
            name: 'Demo Widget',
            price: Number(price),
        });
    }
    return init;
}

/** @returns {number|null} */
function readProductPrice() {
    const el = document.getElementById('productPrice');
    const raw = el?.value?.trim() ?? '';
    const n = Number(raw);
    if (!Number.isFinite(n) || n < 0) {
        alert('Enter a valid non-negative price (e.g. 12.50).');
        el?.focus();
        return null;
    }
    // Keep two-decimal display consistent with typical currency demos.
    el.value = n.toFixed(2);
    return Number(n.toFixed(2));
}

function phaseTag(phase) {
    if (!phase || phase === 'n/a') return '';
    const cls = phase === 'calm' ? 'phase-calm'
              : phase === 'approaching' ? 'phase-apr'
              : phase === 'hold' ? 'phase-hold'
              : '';
    return `<span class="tag ${cls}">${phase}</span>`;
}

function cacheTag(xcache) {
    if (!xcache) return '';

    const isOcHit = /output=hit/i.test(xcache);
    const isFcHit = /data=hit/i.test(xcache);
    const isFcStale = /data=stale/i.test(xcache);

    if (isOcHit) return `<span class="tag hit">OC-HIT</span>`;
    if (isFcHit) {
        return `<span class="tag miss">OC-MISS</span>`
            + `<span class="tag hit" style="margin-left:4px">FC-HIT</span>`;
    }
    if (isFcStale) {
        return `<span class="tag miss">OC-MISS</span>`
            + `<span class="tag phase-hold" style="margin-left:4px">FC-STALE</span>`;
    }
    // Both layers missed — factory path (not a “hit”; avoid FACTORY-HIT wording).
    return `<span class="tag miss">OC-MISS</span>`
        + `<span class="tag miss" style="margin-left:4px">FC-MISS</span>`
        + `<span class="tag factory" style="margin-left:4px" title="Fusion factory ran (GetOrSet miss path)">FACTORY</span>`;
}

function escHtml(s) {
    return String(s ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
}

function escAttr(s) {
    return escHtml(s).replace(/'/g, '&#39;');
}

/**
 * @param {string} url
 * @param {RequestInit} init
 */
async function runRequest(url, init) {
    const method = (init.method || 'GET').toUpperCase();
    const started = performance.now();

    let res;
    try {
        res = await fetch(url, init);
    } catch (err) {
        appendLog({ error: String(err), url: `${method} ${url}` });
        return;
    }

    const ms = Math.round(performance.now() - started);
    const body = await res.text();

    let isBrowserCache = false;
    await new Promise(r => requestAnimationFrame(() => r()));
    const pEntries = performance.getEntriesByName(res.url);
    if (pEntries.length > 0) {
        const lastEntry = pEntries[pEntries.length - 1];
        if (lastEntry.transferSize === 0 && lastEntry.decodedBodySize > 0) {
            isBrowserCache = true;
        }
    }

    const xcache = res.headers.get('x-cache') || '';
    const phaseMatch = xcache.match(/phase=([^;]+)/i);
    const phase = phaseMatch ? phaseMatch[1].trim() : '';

    appendLog({
        isBrowserCache,
        xcache,
        cc: res.headers.get('cache-control') || '',
        etag: res.headers.get('etag') || '',
        demoMs: res.headers.get('x-demo-elapsed-ms') || '',
        ms,
        url: `${method} ${url}`,
        body,
        phase,
        status: res.status,
    });
}

/** Header checkbox — default true: bypass browser HTTP cache; server OC/FC unchanged. */
function isBrowserCacheDisabled() {
    const el = document.getElementById('disableBrowserCache');
    // If the control is missing, prefer server-visible fetches.
    return el ? !!el.checked : true;
}

async function fetchDomainOnce() {
    await runRequest(pickDomainUrl(), buildFetchInit('GET', {
        disableBrowserCache: isBrowserCacheDisabled(),
    }));
}

async function fetchCrudOnce() {
    await runRequest(CRUD_URL, buildFetchInit('GET', {
        disableBrowserCache: isBrowserCacheDisabled(),
    }));
}

function logInvalidateResult(title, body) {
    const entry = document.createElement('div');
    entry.className = 'entry';
    entry.innerHTML = `<div class="meta"><span class="tag phase-hold">${escHtml(title)}</span></div>
<div class="headers">${escHtml(JSON.stringify(body, null, 2))}</div>`;
    logEl.prepend(entry);
    logHintEl.textContent = `${logEl.children.length} request(s)`;
}

function appendLog({ isBrowserCache, xcache, cc, etag, demoMs, ms, url, body, phase, status, error }) {
    const entry = document.createElement('div');
    entry.className = 'entry';

    if (error) {
        entry.innerHTML = `<div class="meta"><span class="tag miss">ERROR</span> ${escHtml(url)}</div>
<div class="headers">${escHtml(error)}</div>`;
    } else {
        const sourceTag = isBrowserCache
            ? `<span class="tag browser">BROWSER-CACHE</span>`
            : cacheTag(xcache);

        entry.innerHTML = `
<div class="meta">
  ${sourceTag}
  ${phaseTag(phase)}
  ${status !== 200 ? `<strong>${status}</strong> ` : ''}<span class="url">${escHtml(url)}</span>
  · ${ms} ms client${demoMs ? ` · ${demoMs} ms server` : ''}
</div>
<div class="headers">cache-control: ${escHtml(cc || '—')}
etag: ${escHtml(etag || '—')}
x-cache: ${escHtml(xcache || '—')}

body: ${escHtml((body || '').slice(0, 600))}</div>`;
    }

    logEl.prepend(entry);
    logHintEl.textContent = `${logEl.children.length} request(s)`;
}

function clearLog() {
    logEl.innerHTML = '';
    logHintEl.textContent = '';
}

// ─── JSON editor modal ───────────────────────────────────────────────────────
async function openEditor() {
    editorErrorEl.classList.add('hidden');
    editorErrorEl.textContent = '';
    try {
        const res = await fetch('/api/demo/appsettings', { cache: 'no-store' });
        const content = await res.text();
        initEditor(content);
        editorOverlay.classList.remove('hidden');
        document.body.classList.add('modal-open');
    } catch (err) {
        alert('Could not load appsettings.json: ' + err);
    }
}

function closeEditor() {
    editorOverlay.classList.add('hidden');
    document.body.classList.remove('modal-open');
}

async function saveSettings() {
    const content = getEditorContent();

    try {
        JSON.parse(content);
    } catch (ex) {
        editorErrorEl.textContent = '⚠ Invalid JSON: ' + ex.message;
        editorErrorEl.classList.remove('hidden');
        return;
    }

    editorErrorEl.classList.add('hidden');

    const res = await fetch('/api/demo/appsettings', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: content,
        cache: 'no-store',
    });

    if (res.ok) {
        closeEditor();
        await loadEndpoints();
        appendLog({
            error: null, xcache: '', cc: '', etag: '', demoMs: '', ms: 0,
            url: 'appsettings.json', body: '', phase: '', status: 200,
            isBrowserCache: false,
        });
        logEl.firstChild.querySelector('.meta').innerHTML =
            `<span class="tag phase-calm">SAVED</span> appsettings.json — config reloaded (no invalidation)`;
    } else {
        let msg = 'Save failed.';
        try {
            const err = await res.json();
            msg = `${err.title}: ${err.detail}`;
        } catch { /* raw response */ }
        editorErrorEl.textContent = '⚠ ' + msg;
        editorErrorEl.classList.remove('hidden');
    }
}

// ─── Event listeners ─────────────────────────────────────────────────────────
btnOnceEl.onclick = () => fetchDomainOnce();
btnTwiceEl.onclick = async () => { await fetchDomainOnce(); await fetchDomainOnce(); };

document.getElementById('btnCrudFetch').onclick = () => fetchCrudOnce();
document.getElementById('btnCrudFetchTwice').onclick = async () => {
    await fetchCrudOnce();
    await fetchCrudOnce();
};
document.getElementById('btnPutProduct').onclick = async () => {
    const price = readProductPrice();
    if (price == null) return;
    // Writes always go to the network.
    await runRequest(CRUD_URL, buildFetchInit('PUT', { disableBrowserCache: true, price }));
};
document.getElementById('btnInvalidateEntity').onclick = async () => {
    const res = await fetch(
        `/api/demo/invalidate-entity/${encodeURIComponent(CRUD_DOMAIN)}/products/42`,
        { method: 'POST', cache: 'no-store' });
    const body = await res.json();
    logInvalidateResult('INVALIDATE ENTITY products/42', body);
};
document.getElementById('btnClear').onclick = clearLog;
document.getElementById('btnCrudClear').onclick = clearLog;
endpointEl.onchange = updateDomainLabel;

document.getElementById('btnInvalidate').onclick = async () => {
    const e = selectedEndpoint();
    const domain = e?.domain ?? '';
    if (!domain) return;
    const res = await fetch(`/api/demo/invalidate/${encodeURIComponent(domain)}`, {
        method: 'POST',
        cache: 'no-store',
    });
    const body = await res.json();
    logInvalidateResult(`INVALIDATE DOMAIN ${domain}`, body);
};

document.getElementById('btnEditSettings').onclick = openEditor;
document.getElementById('btnCloseEditor').onclick  = closeEditor;
document.getElementById('btnCancelEditor').onclick = closeEditor;
document.getElementById('btnSaveSettings').onclick = saveSettings;

editorOverlay.addEventListener('click', (e) => {
    if (e.target === editorOverlay) closeEditor();
});

document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape' && !editorOverlay.classList.contains('hidden')) closeEditor();
});

// ─── Init ────────────────────────────────────────────────────────────────────
setPanel('domain');
loadEndpoints();
