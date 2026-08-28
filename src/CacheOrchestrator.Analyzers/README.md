# CacheOrchestrator.Analyzers

[**CacheOrchestrator**](https://github.com/amarinsek/CacheOrchestrator) is a multi-tier cache coordination and synchronized invalidation library for .NET.

This project contains the Roslyn analyzers for CacheOrchestrator attribute usage. It is **not published as a separate NuGet package**: the analyzer assembly is embedded in `CacheOrchestrator.AspNetCore` and reaches applications transitively through that package or the `CacheOrchestrator` meta package.

## Rules

| Id | Severity | Description |
|----|----------|-------------|
| `COIDENTITY001` | Error | Duplicate HTTP method across `[CacheIdentity]` / `[ContentHashCacheIdentity]` on the same action (including class-level attributes). |

## Usage

No separate registration is required. Install `CacheOrchestrator.AspNetCore` or `CacheOrchestrator`; supported diagnostics then run during compilation.

Within this repository, `CacheOrchestrator.AspNetCore` references the project with `OutputItemType=Analyzer`. Build the analyzer in Release before packing AspNetCore so its `netstandard2.0` assembly can be embedded under `analyzers/dotnet/cs`.

## Documentation

- [Endpoint cache identity](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/reference/cache-identity.md)
- [Documentation index](https://github.com/amarinsek/CacheOrchestrator/blob/main/docs/README.md)
- [Repository](https://github.com/amarinsek/CacheOrchestrator)

## License

MIT — [LICENSE.md](https://github.com/amarinsek/CacheOrchestrator/blob/main/LICENSE.md)
