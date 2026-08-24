# Security Policy

## Reporting a vulnerability

**Do not** file a public GitHub issue for security vulnerabilities.

Please report privately so we can fix and, if needed, coordinate a release before disclosure.

### Preferred

1. Open a **[GitHub Security Advisory](https://github.com/amarinsek/CacheOrchestrator/security/advisories/new)** on this repository (private), **or**
2. Email the maintainer via the address on the [GitHub profile](https://github.com/amarinsek) with subject  
   `CacheOrchestrator security: <short title>`

### Include if possible

- Affected package (`CacheOrchestrator` / `CacheOrchestrator.Redis`) and version
- Target framework and ASP.NET Core version
- Description of the issue and impact
- Minimal reproduction or proof-of-concept
- Whether the issue is already public elsewhere

### What to expect

- Acknowledgement when the report is received (typically within a few days)
- A fix or mitigation plan for confirmed issues
- Credit in the advisory / changelog if you want it (optional)

## Non-security bugs

Use [GitHub Issues](https://github.com/amarinsek/CacheOrchestrator/issues) with the bug template.

## Scope notes for this library

CacheOrchestrator configures and scopes ASP.NET Core Output Cache and FusionCache. Security-sensitive defaults include:

- **Authenticated traffic is not Output-Cached by default** (`AuthBypassMode: AuthenticatedOrAuthorization`). The obsolete `BypassWhenAuthenticated` bool still binds when the mode is unset.
- Client cache is **blocked** for that traffic unless you explicitly opt in

Misconfiguration (e.g. caching private user data as `public` without per-user vary) is an application responsibility — see [docs/guide/faq.md](docs/guide/faq.md) and [docs/reference/output-cache.md](docs/reference/output-cache.md).

### Diagnostic response headers

By default the library emits **`X-Cache`** (hit/miss, domain, schedule phase). This is useful for operations and debugging but is client-visible. To disable diagnostic headers in production:

```json
"Cache": { "EmitDiagnosticsHeaders": false }
```

Metrics and tracing are unaffected. See [docs/reference/observability.md](docs/reference/observability.md).
