# CacheOrchestrator.Analyzers

Roslyn analyzers for CacheOrchestrator attribute usage.

## Rules

| Id | Severity | Description |
|----|----------|-------------|
| `COIDENTITY001` | Error | Duplicate HTTP method across `[CacheIdentity]` / `[ContentHashCacheIdentity]` on the same action (including class-level attributes). |

## Consumption in this repo

Projects that reference `CacheOrchestrator.AspNetCore` via `ProjectReference` get the analyzer when AspNetCore (or the test project) lists:

```xml
<ProjectReference Include="..\CacheOrchestrator.Analyzers\CacheOrchestrator.Analyzers.csproj"
  PrivateAssets="all"
  ReferenceOutputAssembly="false"
  OutputItemType="Analyzer" />
```

## NuGet packing caveats

- This project packs as **development dependency** with `IncludeBuildOutput=false`. The DLL is placed under `analyzers/dotnet/cs` (not `lib/`).
- `CacheOrchestrator.AspNetCore` also attempts to embed the same DLL into its nupkg under `analyzers/dotnet/cs` so AspNetCore package consumers get the analyzer transitively. Pack AspNetCore **after** building Analyzers (`netstandard2.0`), or the `Exists(...)` packing item will skip the file.
- Prefer releasing `CacheOrchestrator.Analyzers` as its own package when you need independent versioning; the AspNetCore embed is a convenience for the default meta package path.
