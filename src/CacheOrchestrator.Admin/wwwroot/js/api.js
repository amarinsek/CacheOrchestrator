/**
 * HTTP client for the Admin App's own REST API (`/api/*`).
 * Errors surface as thrown Error with a readable message for empty states.
 */

/**
 * Fetch JSON from the Admin App API.
 * @param {string} path Absolute path, e.g. `/api/overview`
 * @param {RequestInit} [options]
 * @returns {Promise<any>}
 */
export async function api(path, options = {}) {
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
}
