# Benchmark results

How to run and what the CacheOrchestrator micro-benchmarks measure (`tests/CacheOrchestrator.Benchmarks`).

> **Disclaimer:** absolute numbers (mean ns) depend on CPU, .NET runtime, power plan, and load. They are **not** SLAs and are **not** comparable across machines. Prefer **ratios** (baseline methods), **Allocated**, and before/after on the **same** machine.

## How to run

```bash
dotnet run -c Release --project tests/CacheOrchestrator.Benchmarks
```

Filter examples:

```bash
dotnet run -c Release --project tests/CacheOrchestrator.Benchmarks -- --filter *DomainKey*
dotnet run -c Release --project tests/CacheOrchestrator.Benchmarks -- --filter *XCache*
dotnet run -c Release --project tests/CacheOrchestrator.Benchmarks -- --filter *ClientCache*
dotnet run -c Release --project tests/CacheOrchestrator.Benchmarks -- --filter *HttpHelper*
dotnet run -c Release --project tests/CacheOrchestrator.Benchmarks -- --filter *Policy*
```

All classes use a shared short job (`[ShortJob]`: net10.0, warmup 1 / iteration 3 / launch 1) for consistent local runs. Artifacts default under `_local/BenchmarkDotNet.Artifacts` (not committed).

## Hot paths covered by benchmarks

| Benchmark | What it measures |
|-----------|------------------|
| `DomainKeyGeneratorBenchmarks` | Fusion key materialization (path, query, tracking, encoding, host, **resource id**, **route endpoint**) |
| `HttpHelperBenchmarks` | Tracking query detection, `Cache-Control: no-store` scan, Accept-Encoding normalization |
| `ClientCacheHeaderGeneratorBenchmarks` | Client Cache Schedule `Cache-Control` (Calm / Approaching / **Hold** / **must-revalidate** / NoStore / Private) |
| `XCacheHeaderFormatterBenchmarks` | Diagnostic `X-Cache` formatting (Hit / Miss / **Stale** / **Bypass** / **Blocked** / Hold phase) |
| `NormalizeDomainBenchmarks` | `DomainName.Normalize` and **`NormalizeResourceId`** |
| `CacheETagFactoryBenchmarks` | Weak ETag from version and version+resource |
| `FusionEntryOptionsBenchmarks` | `GetFusionEntryOptions` build/reuse per domain snapshot |
| `DomainCacheOptionsProviderBenchmarks` | Domain options L1 (HttpContext) / L2 (process) hit paths |
| `DomainTemplateCompilerBenchmarks` | Template `GetOrAdd` + per-request resolve |
| `DomainOutputCachePolicyBenchmarks` | `CacheRequestAsync` + **`CollectNonTrackingQueryKeys`** |

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

## Working with results (who publishes what)

**This file is not a dump for every developer’s laptop runs.** Pasting many undated, different-machine tables here becomes noise and false comparisons.

| Audience | What to do |
|----------|------------|
| **Contributors** | Run BDN locally when touching a hot path. Put **before/after** (same machine, Release, filter) in the **PR description** if the change is performance-related. Do **not** commit raw BDN output or `_local/BenchmarkDotNet.Artifacts`. |
| **Maintainers / designated owners** | Optionally add a **curated reference snapshot** below (release, major optimisations, or documenting a known baseline). Always include date, CPU, runtime, OS, and commit/filter. Replace or archive old snapshots; do not accumulate parallel “everyone’s PC” sections. |

Reviewers care about **direction** (faster / fewer allocations on one machine) and **correctness**, not matching absolute ns across developers.

### Template (curated reference only)

```
### YYYY-MM-DD — reference (optional)

- Owner:
- CPU:
- Runtime: .NET x.y
- OS:
- Commit / filter:

| Method | Mean | Allocated |
|--------|------|-----------|
| ...    | ...  | ...       |
```

## Reference snapshots

*(Curated by maintainers only. Leave empty until a deliberate reference run is recorded.)*
