# CacheOrchestrator.Analyzers

Roslyn analyzers for CacheOrchestrator attribute usage. **Not published as a separate NuGet package** — the DLL is embedded into `CacheOrchestrator.AspNetCore` under `analyzers/dotnet/cs`.

## Rules

| Id | Severity | Description |
|----|----------|-------------|
| `COIDENTITY001` | Error | Duplicate HTTP method across `[CacheIdentity]` / `[ContentHashCacheIdentity]` on the same action (including class-level attributes). |

## Consumption

- **This repo:** `CacheOrchestrator.AspNetCore` (and its unit tests) reference the project as an analyzer (`OutputItemType=Analyzer`).
- **NuGet consumers:** installing `CacheOrchestrator.AspNetCore` (or the meta `CacheOrchestrator` package) gets the analyzer transitively from the AspNetCore nupkg. Pack AspNetCore after a Release build of Analyzers (`netstandard2.0`) so the embed `Exists(...)` path finds the DLL.
