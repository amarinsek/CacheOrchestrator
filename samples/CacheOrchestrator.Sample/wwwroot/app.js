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


async function loadEndpoints() {
    endpoints = await (await fetch('/api/demo/endpoints')).json();
    endpointEl.innerHTML = endpoints.map(e =>
        `<option value="${e.url}">${e.label || e.url} (${e.domain})</option>`
    ).join('');
    updateDomainLabel();
}

function updateDomainLabel() {
    const url = endpointEl.value;
    const e = endpoints.find(x => x.url === url);
    domainNameEl.textContent = e?.domain ?? '—';
    document.getElementById('domainBackend').textContent = e?.backend ?? '…';
}

// ─── Fetch helpers ───────────────────────────────────────────────────────────
function pickUrl() {
    let url = endpointEl.value;
    // Replace path parameters with demo values so the URL is valid
    url = url.replace(/{id}/g, '42').replace(/{[^}]+}/g, 'demo');
    const extra = document.getElementById('extraQuery').value.trim();
    if (extra) url += (url.includes('?') ? '&' : '?') + extra.replace(/^\?/, '');
    if (document.getElementById('utm').checked)
        url += (url.includes('?') ? '&' : '?') + 'utm_source=demo';
    return url;
}

function buildHeaders() {
    const h = {};
    if (document.getElementById('nostore').checked) h['Cache-Control'] = 'no-store';
    return h;
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

    if (isOcHit) return `<span class="tag hit">OC-HIT</span>`;
    if (isFcHit) return `<span class="tag miss">OC-MISS</span><span class="tag hit" style="margin-left:4px">FC-HIT</span>`;
    
    return `<span class="tag miss">MISS</span>`;
}

async function fetchOnce() {
    const url = pickUrl();
    const started = performance.now();

    let res;
    try {
        res = await fetch(url, {
            headers: buildHeaders()
        });
    } catch (err) {
        appendLog({ error: String(err), url });
        return;
    }

    const ms        = Math.round(performance.now() - started);
    const body      = await res.text();
    const hitId     = res.headers.get('x-demo-hit-id') || '';

    // Reliably detect browser cache (no network transfer)
    let isBrowserCache = false;
    const pEntries = performance.getEntriesByName(res.url);
    if (pEntries.length > 0) {
        // fetch() entries are added after the promise resolves
        const lastEntry = pEntries[pEntries.length - 1];
        if (lastEntry.transferSize === 0) {
            isBrowserCache = true;
        }
    }

    const xcache  = res.headers.get('x-cache') || '';
    const cc      = res.headers.get('cache-control') || '';
    const etag    = res.headers.get('etag') || '';
    const demoMs  = res.headers.get('x-demo-elapsed-ms') || '';

    // Extract phase from x-cache header (e.g. "phase=calm")
    const phaseMatch = xcache.match(/phase=([^;]+)/i);
    const phase = phaseMatch ? phaseMatch[1].trim() : '';

    appendLog({ isBrowserCache, xcache, cc, etag, demoMs, ms, url, body, phase, status: res.status, hitId });
}

function appendLog({ isBrowserCache, xcache, cc, etag, demoMs, ms, url, body, phase, status, hitId, error }) {
    const entry = document.createElement('div');
    entry.className = 'entry';

    if (error) {
        entry.innerHTML = `<div class="meta"><span class="tag miss">ERROR</span> ${url}</div>
<div class="headers">${error}</div>`;
    } else {
        const sourceTag = isBrowserCache
            ? `<span class="tag hit">BROWSER-CACHE</span>`
            : cacheTag(xcache);

        entry.innerHTML = `
<div class="meta">
  ${sourceTag}
  ${phaseTag(phase)}
  ${status !== 200 ? `<strong>${status}</strong> ` : ''}<span class="url">${url}</span>
  · ${ms} ms client${demoMs ? ` · ${demoMs} ms server` : ''}
</div>
<div class="headers">cache-control: ${cc || '—'}
etag: ${etag || '—'}
x-cache: ${xcache || '—'}
x-demo-hit-id: ${hitId || '—'}

body: ${body.slice(0, 600)}</div>`;
    }

    logEl.prepend(entry);
    logHintEl.textContent = `${logEl.children.length} request(s)`;
}

// ─── JSON editor modal ───────────────────────────────────────────────────────
async function openEditor() {
    editorErrorEl.classList.add('hidden');
    editorErrorEl.textContent = '';
    try {
        const res = await fetch('/api/demo/appsettings');
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

    // Client-side JSON validation before sending
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
        body: content
    });

    if (res.ok) {
        closeEditor();
        // Reload endpoints list in case Demo:Endpoints changed
        await loadEndpoints();
        appendLog({ error: null, xcache: '', cc: '', etag: '', demoMs: '', ms: 0,
            url: 'appsettings.json', body: '', phase: '', status: 200, hitId: '' });
        logEl.firstChild.querySelector('.meta').innerHTML =
            `<span class="tag phase-calm">SAVED</span> appsettings.json — config reloaded &amp; domains invalidated`;
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
document.getElementById('btnOnce').onclick    = () => fetchOnce();
document.getElementById('btnTwice').onclick   = async () => { await fetchOnce(); await fetchOnce(); };
document.getElementById('btnClear').onclick   = () => { logEl.innerHTML = ''; logHintEl.textContent = ''; };
endpointEl.onchange                           = updateDomainLabel;

document.getElementById('btnInvalidate').onclick = async () => {
    const url = endpointEl.value;
    const domain = endpoints.find(x => x.url === url)?.domain ?? '';
    if (!domain) return;
    const res = await fetch(`/api/demo/invalidate/${encodeURIComponent(domain)}`, { method: 'POST' });
    const body = await res.json();
    const entry = document.createElement('div');
    entry.className = 'entry';
    entry.innerHTML = `<div class="meta"><span class="tag phase-hold">INVALIDATE</span> ${domain}</div>
<div class="headers">${JSON.stringify(body, null, 2)}</div>`;
    logEl.prepend(entry);
    logHintEl.textContent = `${logEl.children.length} request(s)`;
};

document.getElementById('btnEditSettings').onclick = openEditor;
document.getElementById('btnCloseEditor').onclick  = closeEditor;
document.getElementById('btnCancelEditor').onclick = closeEditor;
document.getElementById('btnSaveSettings').onclick = saveSettings;

// Close modal on overlay click
editorOverlay.addEventListener('click', (e) => {
    if (e.target === editorOverlay) closeEditor();
});

// Close on Escape
document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape' && !editorOverlay.classList.contains('hidden')) closeEditor();
});

// ─── Init ────────────────────────────────────────────────────────────────────
loadEndpoints();