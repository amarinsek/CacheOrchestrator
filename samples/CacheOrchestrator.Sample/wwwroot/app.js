import { EditorView, basicSetup } from 'codemirror';
import { json } from '@codemirror/lang-json';
import { oneDark } from '@codemirror/theme-one-dark';

// ─── DOM refs ───────────────────────────────────────────────────────────────
const editorOverlay   = document.getElementById('editorOverlay');
const editorMount     = document.getElementById('editorMount');
const editorErrorEl   = document.getElementById('editorError');
const logHintEl       = document.getElementById('logHint');
const responseTitleEl = document.getElementById('responseTitle');
const panelGettingStarted = document.getElementById('panelGettingStarted');
const panelVary       = document.getElementById('panelVary');
const panelPost       = document.getElementById('panelPost');
const promotionsBackendEl = document.getElementById('promotionsBackend');
const crudBackendEl   = document.getElementById('crudBackend');
const varyBackendEl   = document.getElementById('varyBackend');
const postBackendEl   = document.getElementById('postBackend');

const PROMOTIONS_URL = '/api/promotions';
const CRUD_URL = '/api/products/42';
const VARY_URL = '/api/vary-demo';
const VARY_DOMAIN = 'vary-demo';
const POST_SEARCH_URL = '/api/demo/search';
const POST_CREATE_URL = '/api/demo/products';
const POST_DOMAIN = 'product-search';
const DEMO_REQUEST_ID_HEADER = 'X-Demo-Request-Id';

const responseLogs = {
    'getting-started': document.getElementById('logGettingStarted'),
    vary: document.getElementById('logVary'),
    post: document.getElementById('logPost'),
};
const responseTitles = {
    'getting-started': 'Getting started responses',
    vary: 'Vary responses',
    post: 'POST identity responses',
};
let activePanelName = 'getting-started';

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

async function loadEndpoints() {
    const all = await (await fetch('/api/demo/endpoints', { cache: 'no-store' })).json();
    const promotions = all.find(e => e.group === 'getting-started' && e.url === PROMOTIONS_URL);
    const crud = all.find(e => e.group === 'getting-started' && (e.url || '').startsWith('/api/products/'));
    const vary = all.find(e => e.group === 'vary');

    if (promotionsBackendEl) {
        promotionsBackendEl.textContent = promotions?.backend || '…';
    }
    if (crudBackendEl) {
        crudBackendEl.textContent = crud?.backend || '…';
    }
    if (varyBackendEl) {
        varyBackendEl.textContent = vary?.backend || '…';
    }

    const postMeta = all.find(e => e.group === 'post' && e.domain === POST_DOMAIN && (e.url || '').includes('/api/demo/search'))
        || all.find(e => e.domain === POST_DOMAIN);
    if (postBackendEl) {
        postBackendEl.textContent = postMeta?.backend || '…';
    }
}

// ─── Panel switch ────────────────────────────────────────────────────────────
function setPanel(name) {
    const panels = [
        { el: panelGettingStarted, key: 'getting-started' },
        { el: panelVary, key: 'vary' },
        { el: panelPost, key: 'post' },
    ];
    for (const p of panels) {
        const on = p.key === name;
        p.el?.classList.toggle('hidden', !on);
        if (p.el) p.el.hidden = !on;
    }

    document.querySelectorAll('.panel-tab').forEach((tab) => {
        const on = tab.dataset.panel === name;
        tab.classList.toggle('active', on);
        tab.setAttribute('aria-selected', on ? 'true' : 'false');
    });

    activePanelName = name;
    for (const [key, log] of Object.entries(responseLogs)) {
        const on = key === name;
        log?.classList.toggle('hidden', !on);
        if (log) {
            log.hidden = !on;
            log.setAttribute('aria-hidden', on ? 'false' : 'true');
        }
    }
    responseTitleEl.textContent = responseTitles[name] || 'Responses';
    updateLogHint();
}

document.querySelectorAll('.panel-tab').forEach((tab) => {
    tab.addEventListener('click', () => setPanel(tab.dataset.panel));
});

// ─── Fetch helpers ───────────────────────────────────────────────────────────
function buildVaryUrl() {
    let url = VARY_URL;
    const extra = document.getElementById('extraQuery').value.trim();
    if (extra) url += (url.includes('?') ? '&' : '?') + extra.replace(/^\?/, '');
    return url;
}

/**
 * Fetch options for playground requests.
 * Default cache: 'no-store' (server OC/DC visible). Checkbox uses cache: 'default' for client max-age demos.
 *
 * @param {'GET'|'PUT'|'POST'} [method]
 * @param {{ disableBrowserCache?: boolean, price?: number, jsonBody?: object }} [opts]
 */
function buildFetchInit(method = 'GET', { disableBrowserCache = true, price, jsonBody } = {}) {
    /** @type {RequestInit} */
    const init = {
        method,
        // Browser fetch cache mode only; server OC/DC still run normally.
        cache: disableBrowserCache ? 'no-store' : 'default',
        headers: {
            [DEMO_REQUEST_ID_HEADER]: crypto.randomUUID(),
        },
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
    if (method === 'POST' && jsonBody !== undefined) {
        init.headers = { 'Content-Type': 'application/json', ...init.headers };
        init.body = JSON.stringify(jsonBody);
    }
    return init;
}

function readPostSearchBody() {
    const q = document.getElementById('postSearchQ')?.value?.trim() ?? '';
    const sort = document.getElementById('postSearchSort')?.value || 'relevance';
    const pageRaw = Number(document.getElementById('postSearchPage')?.value);
    const page = Number.isFinite(pageRaw) && pageRaw >= 1 ? Math.floor(pageRaw) : 1;
    const pageEl = document.getElementById('postSearchPage');
    if (pageEl) pageEl.value = String(page);
    const uiHint = document.getElementById('postSearchUiHint')?.value ?? '';
    return { q, sort, page, uiHint };
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

function cacheOrchestratorHeaderToken(cacheOrchestratorHeader, name) {
    const m = cacheOrchestratorHeader.match(new RegExp('(?:^|[;\\s])' + name + '=([^;\\s]+)', 'i'));
    return m ? m[1].trim().toLowerCase() : '';
}

function cacheTag(cacheOrchestratorHeader) {
    if (!cacheOrchestratorHeader) return '';

    const oc = cacheOrchestratorHeaderToken(cacheOrchestratorHeader, 'oc') || cacheOrchestratorHeaderToken(cacheOrchestratorHeader, 'output');
    const dc = cacheOrchestratorHeaderToken(cacheOrchestratorHeader, 'dc') || cacheOrchestratorHeaderToken(cacheOrchestratorHeader, 'data') || cacheOrchestratorHeaderToken(cacheOrchestratorHeader, 'fc');
    const faRun = cacheOrchestratorHeaderToken(cacheOrchestratorHeader, 'fa') === 'run'
        || (!!dc && dc !== 'hit');

    if (oc === 'hit') return `<span class="tag hit">OC-HIT</span>`;

    let html = '';
    if (oc === 'miss') {
        html = `<span class="tag miss">OC-MISS</span>`;
    } else if (oc === 'off') {
        html = `<span class="tag off">OC-OFF</span>`;
    } else if (oc === 'bypass') {
        html = `<span class="tag phase-apr">OC-BYPASS</span>`;
    }

    if (dc === 'hit') {
        html += `<span class="tag hit" style="margin-left:4px">DC-HIT</span>`;
    } else if (dc === 'stale') {
        html += `<span class="tag phase-hold" style="margin-left:4px">DC-STALE</span>`;
    } else if (dc === 'off') {
        html += `<span class="tag off" style="margin-left:4px">DC-OFF</span>`;
    } else if (dc === 'bypass') {
        html += `<span class="tag phase-apr" style="margin-left:4px">DC-BYPASS</span>`;
    } else if (dc === 'unresolved') {
        html += `<span class="tag miss" style="margin-left:4px">DC-UNRESOLVED</span>`;
    } else if (dc === 'miss') {
        html += `<span class="tag miss" style="margin-left:4px">DC-MISS</span>`;
    }

    if (faRun) {
        html += `<span class="tag factory" style="margin-left:4px" title="Application/origin work ran">FACTORY</span>`;
    }
    return html;
}

function detectEdgeCache(headers) {
    const cloudflareStatus = (headers.get('cf-cache-status') || '').trim().toUpperCase();
    if (cloudflareStatus) {
        const state = cloudflareStatus === 'HIT'
            ? 'hit'
            : ['STALE', 'UPDATING', 'REVALIDATED'].includes(cloudflareStatus)
                ? 'refresh'
                : 'miss';
        return {
            provider: 'Cloudflare',
            status: cloudflareStatus,
            state,
            isCachedResponse: state !== 'miss',
        };
    }

    const cacheStatus = headers.get('cache-status') || '';
    for (const member of cacheStatus.split(',')) {
        const parts = member.split(';').map(part => part.trim()).filter(Boolean);
        if (parts.length < 2) continue;

        const provider = parts[0].replace(/^"|"$/g, '') || 'Edge';
        const isHit = parts.slice(1).some(part => /^hit(?:=\?1)?$/i.test(part));
        const forwarded = parts.slice(1).find(part => /^fwd=/i.test(part));
        const isRefresh = parts.slice(1).some(part =>
            /^(?:detail=)?"?(?:stale|updating|revalidated)"?$/i.test(part))
            || /^fwd=stale$/i.test(forwarded || '');
        const state = isRefresh ? 'refresh' : (isHit ? 'hit' : 'miss');
        return {
            provider,
            status: parts.slice(1).join('; ').toUpperCase() || 'MISS',
            state,
            isCachedResponse: state !== 'miss',
        };
    }

    return null;
}

function edgeTag(edgeCache) {
    if (!edgeCache) return '';

    const cssClass = edgeCache.state === 'hit'
        ? 'hit'
        : edgeCache.state === 'refresh'
            ? 'refresh'
            : 'miss';
    const label = edgeCache.state === 'hit'
        ? 'EDGE-HIT'
        : edgeCache.state === 'refresh'
            ? 'EDGE-REFRESH'
            : 'EDGE-MISS';
    return `<span class="tag ${cssClass}" title="${escHtml(`${edgeCache.provider} ${edgeCache.status}`)}">${label}</span>`;
}

function escHtml(s) {
    return String(s ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
}

/**
 * @param {string} url
 * @param {RequestInit} init
 * @param {'getting-started'|'vary'|'post'} responsePanel
 */
async function runRequest(url, init, responsePanel) {
    const method = (init.method || 'GET').toUpperCase();
    const requestId = new Headers(init.headers).get(DEMO_REQUEST_ID_HEADER) || '';
    const started = performance.now();

    let res;
    try {
        res = await fetch(url, init);
    } catch (err) {
        appendLog({ error: String(err), url: `${method} ${url}` }, responsePanel);
        return;
    }

    const ms = Math.round(performance.now() - started);
    const body = await res.text();

    const echoedRequestId = res.headers.get(DEMO_REQUEST_ID_HEADER) || '';
    const edgeCache = detectEdgeCache(res.headers);
    let isBrowserCache = false;

    // Keep Resource Timing as a fallback for environments that strip the demo
    // echo header. It is useful supporting evidence, but not reliable enough
    // to be the primary browser-cache signal on its own.
    await new Promise(r => requestAnimationFrame(() => r()));
    const pEntries = performance.getEntriesByName(res.url);
    if (pEntries.length > 0) {
        const lastEntry = pEntries[pEntries.length - 1];
        if (lastEntry.transferSize === 0 && lastEntry.decodedBodySize > 0) {
            isBrowserCache = true;
        }
    }

    // An edge hit also returns the request ID stored with the cached origin
    // response. Treat that mismatch as browser cache only when no edge cache
    // reports a hit; Resource Timing above takes precedence for a browser-cached
    // copy of an earlier edge hit.
    if (!isBrowserCache
        && requestId !== ''
        && echoedRequestId !== requestId
        && !edgeCache?.isCachedResponse) {
        isBrowserCache = true;
    }

    const cacheOrchestratorHeader = res.headers.get('x-cacheorchestrator') || '';
    const phaseMatch = cacheOrchestratorHeader.match(/phase=([^;]+)/i);
    const phase = phaseMatch ? phaseMatch[1].trim() : '';

    appendLog({
        isBrowserCache,
        edgeCache,
        edgeCacheApplicable: method === 'GET' || method === 'HEAD',
        cacheOrchestratorHeader,
        cc: res.headers.get('cache-control') || '',
        etag: res.headers.get('etag') || '',
        demoMs: res.headers.get('x-demo-elapsed-ms') || '',
        ms,
        url: `${method} ${url}`,
        body,
        phase,
        status: res.status,
    }, responsePanel);
}

/** Header checkbox — default true: bypass browser HTTP cache; server OC/DC unchanged. */
function isBrowserCacheDisabled() {
    const el = document.getElementById('disableBrowserCache');
    // If the control is missing, prefer server-visible fetches.
    return el ? !!el.checked : true;
}

async function fetchVaryOnce() {
    await runRequest(buildVaryUrl(), buildFetchInit('GET', {
        disableBrowserCache: isBrowserCacheDisabled(),
    }), 'vary');
}

async function fetchPromotionsOnce() {
    await runRequest(PROMOTIONS_URL, buildFetchInit('GET', {
        disableBrowserCache: isBrowserCacheDisabled(),
    }), 'getting-started');
}

async function fetchCrudOnce() {
    await runRequest(CRUD_URL, buildFetchInit('GET', {
        disableBrowserCache: isBrowserCacheDisabled(),
    }), 'getting-started');
}

function logInvalidateResult(title, body, responsePanel) {
    const log = responseLogs[responsePanel];
    const entry = document.createElement('div');
    entry.className = 'entry';
    entry.innerHTML = `<div class="meta"><span class="tag phase-hold">${escHtml(title)}</span></div>
<div class="headers">${escHtml(JSON.stringify(body, null, 2))}</div>`;
    log.prepend(entry);
    updateLogHint();
}

function appendLog({ isBrowserCache, edgeCache, edgeCacheApplicable, cacheOrchestratorHeader, cc, etag, demoMs, ms, url, body, phase, status, error }, responsePanel) {
    const log = responseLogs[responsePanel];
    const entry = document.createElement('div');
    entry.className = 'entry';

    if (error) {
        entry.innerHTML = `<div class="meta"><span class="tag miss">ERROR</span> ${escHtml(url)}</div>
<div class="headers">${escHtml(error)}</div>`;
    } else {
        const originTags = cacheTag(cacheOrchestratorHeader);
        const applicableEdgeCache = edgeCacheApplicable ? edgeCache : null;
        const edgeTags = edgeTag(applicableEdgeCache);
        const sourceTags = isBrowserCache
            ? `<span class="tag browser">BROWSER-CACHE</span>`
            : applicableEdgeCache && applicableEdgeCache.state !== 'miss'
                ? edgeTags
                : edgeTags + originTags;
        const displayedCacheOrchestratorHeader = isBrowserCache
            ? '— (browser cache; server was not contacted)'
            : (cacheOrchestratorHeader || '—');
        const displayedServerMs = isBrowserCache ? '' : demoMs;

        entry.innerHTML = `
<div class="meta">
  ${sourceTags}
  ${phaseTag(isBrowserCache ? '' : phase)}
  ${status !== 200 ? `<strong>${status}</strong> ` : ''}<span class="url">${escHtml(url)}</span>
  · ${ms} ms client${displayedServerMs ? ` · ${displayedServerMs} ms server` : ''}
</div>
<div class="headers">cache-control: ${escHtml(cc || '—')}
etag: ${escHtml(etag || '—')}
x-cacheorchestrator: ${escHtml(displayedCacheOrchestratorHeader)}

body: ${escHtml((body || '').slice(0, 600))}</div>`;
    }

    log.prepend(entry);
    updateLogHint();
    return entry;
}

function updateLogHint() {
    const count = responseLogs[activePanelName]?.children.length || 0;
    logHintEl.textContent = count > 0 ? `${count} request(s)` : '';
}

function clearLog(responsePanel) {
    responseLogs[responsePanel].innerHTML = '';
    updateLogHint();
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
    const responsePanel = activePanelName;
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
        const entry = appendLog({
            error: null, edgeCache: null, cacheOrchestratorHeader: '', cc: '', etag: '', demoMs: '', ms: 0,
            url: 'appsettings.json', body: '', phase: '', status: 200,
            isBrowserCache: false,
        }, responsePanel);
        entry.querySelector('.meta').innerHTML =
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
document.getElementById('btnPromotionsFetch').onclick = () => fetchPromotionsOnce();
document.getElementById('btnPromotionsFetchTwice').onclick = async () => {
    await fetchPromotionsOnce();
    await fetchPromotionsOnce();
};

document.getElementById('btnCrudFetch').onclick = () => fetchCrudOnce();
document.getElementById('btnCrudFetchTwice').onclick = async () => {
    await fetchCrudOnce();
    await fetchCrudOnce();
};
document.getElementById('btnPutProduct').onclick = async () => {
    const price = readProductPrice();
    if (price == null) return;
    // Writes always go to the network.
    await runRequest(CRUD_URL, buildFetchInit('PUT', { disableBrowserCache: true, price }), 'getting-started');
};
document.getElementById('btnGettingStartedClear').onclick = () => clearLog('getting-started');
document.getElementById('btnVaryOnce').onclick = () => fetchVaryOnce();
document.getElementById('btnVaryTwice').onclick = async () => {
    await fetchVaryOnce();
    await fetchVaryOnce();
};
document.getElementById('btnVaryClear').onclick = () => clearLog('vary');
document.getElementById('btnPostClear').onclick = () => clearLog('post');

async function fetchPostSearchOnce() {
    const disableBrowserCache = document.getElementById('disableBrowserCache')?.checked ?? true;
    const body = readPostSearchBody();
    await runRequest(POST_SEARCH_URL, buildFetchInit('POST', { disableBrowserCache, jsonBody: body }), 'post');
}

document.getElementById('btnPostSearchOnce').onclick = () => fetchPostSearchOnce();
document.getElementById('btnPostSearchTwice').onclick = async () => {
    await fetchPostSearchOnce();
    await fetchPostSearchOnce();
};
document.getElementById('btnPostCreate').onclick = async () => {
    const disableBrowserCache = document.getElementById('disableBrowserCache')?.checked ?? true;
    await runRequest(POST_CREATE_URL, buildFetchInit('POST', {
        disableBrowserCache,
        jsonBody: { name: 'New item' },
    }), 'post');
};

document.getElementById('btnVaryInvalidate').onclick = async () => {
    const res = await fetch(`/api/demo/invalidate/${encodeURIComponent(VARY_DOMAIN)}`, {
        method: 'POST',
        cache: 'no-store',
    });
    const body = await res.json();
    logInvalidateResult(`INVALIDATE DOMAIN ${VARY_DOMAIN}`, body, 'vary');
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
setPanel('getting-started');
loadEndpoints();
