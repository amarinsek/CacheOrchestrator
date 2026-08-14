/**
 * HTTP client for the Admin App's own REST API (`/api/*`).
 * Errors surface as thrown Error with a readable message for empty states.
 */

/** In-flight GET dedupe (soft refresh + header share one overview call). */
const inflightGet = new Map();

/**
 * Fetch JSON from the Admin App API.
 * @param {string} path Absolute path, e.g. `/api/overview`
 * @param {RequestInit} [options]
 * @returns {Promise<any>}
 */
export async function api(path, options = {}) {
  const method = (options.method || "GET").toUpperCase();
  const canDedupe = method === "GET" && !options.body;
  if (canDedupe && inflightGet.has(path)) {
    return inflightGet.get(path);
  }

  const run = (async () => {
    const res = await fetch(path, {
      headers: { "Content-Type": "application/json", ...(options.headers || {}) },
      ...options,
    });
    const text = await res.text();
    let body = null;
    try {
      body = text ? JSON.parse(text) : null;
    } catch {
      body = text;
    }
    if (!res.ok) {
      const msg = body && body.error ? body.error : (text || res.statusText);
      throw new Error(msg);
    }
    return body;
  })();

  if (canDedupe) {
    inflightGet.set(path, run);
    try {
      return await run;
    } finally {
      inflightGet.delete(path);
    }
  }
  return run;
}

/** Normalize instance status from API (string enum or numeric). */
export function instanceStatus(s) {
  if (s === 0 || s === "Healthy" || s === "healthy") return "Healthy";
  if (s === 1 || s === "Degraded" || s === "degraded") return "Degraded";
  if (s === 2 || s === "Down" || s === "down") return "Down";
  return s == null || s === "" ? "Down" : String(s);
}
