# Benchmark results

Machine-agnostic notes for CacheOrchestrator micro-benchmarks (`tests/CacheOrchestrator.Benchmarks`).

> **Disclaimer:** absolute numbers depend on CPU, .NET runtime, power plan, and load. Treat these as relative guides, not SLAs. Re-run locally with BenchmarkDotNet before drawing conclusions.

## How to run

```bash
dotnet run -c Release --project tests/CacheOrchestrator.Benchmarks
```

Filter examples:

```bash
dotnet run -c Release --project tests/CacheOrchestrator.Benchmarks -- --filter *DomainKey*
dotnet run -c Release --project tests/CacheOrchestrator.Benchmarks -- --filter *XCache*
dotnet run -c Release --project tests/CacheOrchestrator.Benchmarks -- --filter *ClientCache*
```

## Hot paths covered by benchmarks

| Benchmark | What it measures |
|-----------|------------------|
| `DomainKeyGeneratorBenchmarks` | Fusion key materialization (path + query + encoding) |
| `XCacheHeaderFormatterBenchmarks` | Diagnostic `X-Cache` header formatting |
| `ClientCacheHeaderGeneratorBenchmarks` | Client Cache Schedule `Cache-Control` build (Calm / Approaching) |

## Performance engineering notes (library)

These are intentional hot-path choices in the library (not full BDN numbers):

| Area | Approach |
|------|----------|
| Fusion entry options | `DomainCacheOptions.GetFusionEntryOptions()` builds once per domain snapshot and reuses the same `FusionCacheEntryOptions` instance |
| Tracking query params | `HttpHelper.IsTrackingParameter` uses a fixed prefix array + manual loop (no LINQ/`HashSet` enumerator) |
| `Cache-Control: no-store` | `HttpHelper.ContainsCacheDirective` scans `StringValues` without `ToString()` |
| Domain templates | Parse plans cached per template; resolvers without custom providers are shared; **custom providers are never stored under the template-only key** (avoids provider poisoning) |
| `X-Cache` header | `string.Create` single allocation |
| Output Cache query vary | Non-tracking keys collected without LINQ `Where` |

## Capturing your own results

1. Run BDN in Release on a quiet machine.
2. Copy the summary table from the console (or `--exporters markdown`).
3. Paste below under a dated section with CPU / runtime notes.

### Template

```
### YYYY-MM-DD — local

- CPU:
- Runtime: .NET x.y
- OS:

| Method | Mean | Allocated |
|--------|------|-----------|
| ...    | ...  | ...       |
```
