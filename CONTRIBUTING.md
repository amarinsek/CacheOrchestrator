# Contributing to CacheOrchestrator

Thanks for helping improve **CacheOrchestrator**. This guide covers build, test, coding style, and pull requests.

## Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/download) that supports **net8.0** and **net10.0** (SDK 10.x is fine for multi-target builds). Libraries and library tests multi-target both; **Admin Console App** and its unit tests are **net10.0 only**; samples are net10-only.
- Optional: Docker (Redis integration tests / sample)

## Clone and build

```bash
git clone https://github.com/amarinsek/CacheOrchestrator.git
cd CacheOrchestrator

dotnet restore CacheOrchestrator.slnx
dotnet build CacheOrchestrator.slnx -c Release
```

## Tests

```bash
# Unit tests — multi-target net8.0 + net10.0 per library package.
dotnet test tests/CacheOrchestrator.UnitTests -c Release
dotnet test tests/CacheOrchestrator.Redis.UnitTests -c Release
dotnet test tests/CacheOrchestrator.Bus.UnitTests -c Release
dotnet test tests/CacheOrchestrator.EFCore.Invalidation.UnitTests -c Release
# Or one TFM, e.g.:
dotnet test tests/CacheOrchestrator.UnitTests -c Release -f net8.0
dotnet test tests/CacheOrchestrator.UnitTests -c Release -f net10.0

# Admin Console App tests are net10.0 only
dotnet test tests/CacheOrchestrator.AdminConsole.UnitTests -c Release -f net10.0

# Integration tests — multi-target net8.0 + net10.0 (matches published library TFMs)
# - InMemory tests: no Docker
# - Redis tests: Testcontainers starts redis:7-alpine (Docker required)
dotnet test tests/CacheOrchestrator.IntegrationTests -c Release
# Or one TFM:
dotnet test tests/CacheOrchestrator.IntegrationTests -c Release -f net8.0
dotnet test tests/CacheOrchestrator.IntegrationTests -c Release -f net10.0
```

### Redis integration tests (Testcontainers)

| Requirement | Detail |
|-------------|--------|
| Docker | Engine running (Docker Desktop, or GitHub Actions `ubuntu-latest`) |
| Image | `redis:7-alpine` (pulled on first run) |
| Shared fixture | `[Collection("Redis")]` + `RedisFixture` (one container per collection) |
| Multi-instance | `FusionCacheMultiInstanceRedisTests` starts **two** containers |

If Docker is down, Redis tests fail with a clear message pointing at Docker — they are **not** skipped silently (so CI cannot go green without Redis coverage).

CI (`build.yml` / `publish.yml`):

1. `docker version` / `docker info`
2. `dotnet test` IntegrationTests (includes Testcontainers Redis)

Micro-benchmarks (optional):

```bash
dotnet run -c Release --project tests/CacheOrchestrator.Benchmarks
```

See [docs/benchmarks/results.md](docs/benchmarks/results.md).

## Samples

```bash
# One-minute InMemory demo (MISS → HIT)
dotnet run --project samples/CacheOrchestrator.Minimal

# Interactive playground (TTL, schedule, Redis, CRUD)
dotnet run --project samples/CacheOrchestrator.Sample

# Same check as CI (miss → hit on /hello; needs prior Release build)
dotnet build samples/CacheOrchestrator.Minimal -c Release
bash samples/CacheOrchestrator.Minimal/smoke.sh
```

## Project layout

| Path | Role |
|------|------|
| `src/CacheOrchestrator` | Core library (InMemory; no Redis package references) |
| `src/CacheOrchestrator.Redis` | Optional Redis backends |
| `tests/*` | Unit, integration, benchmarks |
| `samples/*` | Playground app |
| `docs/*` | Human technical docs |

Agent-oriented conventions live in [AGENTS.md](AGENTS.md) (same rules for human contributors).

## Coding conventions

- **English only** for code comments and XML documentation.
- Follow the repo **[`.editorconfig`](.editorconfig)** (style, analyzers, formatting). Before opening a PR, run:

  ```bash
  dotnet format CacheOrchestrator.slnx
  ```

  Prefer fixing what the IDE / `dotnet format` reports rather than inventing local style rules.

Project-level rules that are easy to miss:

- Public config keys bound from appsettings are a **public contract** — rename only with a breaking-change plan and docs/changelog updates.
- Interfaces live next to implementations (there is **no** separate `CacheOrchestrator.Abstractions` assembly).
- Library code: **`ConfigureAwait(false)`** on awaits; prefer **`sealed`** public concrete types where practical.
- Keep public API surface small: apps depend on interfaces + DI; default services stay **internal**.



## Documentation

When you change public API, config keys, or behaviour:

1. Update the relevant file under `docs/`
2. Update the root [README.md](README.md) if the try/minimal path or feature list changes
3. Add a note under `[Unreleased]` in [CHANGELOG.md](CHANGELOG.md)

### NuGet packaging (SourceLink + symbols + release notes)

Packable projects inherit from `Directory.Build.props`:

| Setting | Source |
|---------|--------|
| **Version** | **Git tags** via [MinVer](https://github.com/adamralph/minver) (`v1.0.0` → `1.0.0`) |
| Authors, license, repo URL | `Directory.Build.props` |
| Package **description** | each `.csproj` (`Description`) |
| Package **readme** (NuGet UI) | `src/CacheOrchestrator*.csproj` `README.md` (`PackageReadmeFile`) — **not** the root GitHub README |
| Package **release notes** (NuGet UI) | **`PACKAGE_RELEASE_NOTES.md`** → `PackageReleaseNotes` |
| SourceLink + **`.snupkg`** | `Directory.Build.props` + `Microsoft.SourceLink.GitHub` |

`PackageReleaseNotes` is **not** auto-generated from `CHANGELOG.md`.  
Full release procedure: **[docs/releasing.md](docs/releasing.md)**.

#### Release checklist (short)

1. Update **CHANGELOG.md** and **PACKAGE_RELEASE_NOTES.md**.
2. Commit on `main`.
3. Tag **`v{version}`** (e.g. `v1.0.0`) and push the tag.
4. Publish **GitHub Release** for that tag → `publish.yml` packs and pushes to NuGet and builds the Admin Console image on GHCR
   (Trusted Publishing / OIDC — configure policy on nuget.org; no `NUGET_API_KEY` secret required).
5. Confirm nuget.org (version from tag, release notes, symbols).

Do **not** set `<Version>` manually in `Directory.Build.props` — MinVer owns it.

Both packages share the same version and `PACKAGE_RELEASE_NOTES.md`.

#### Optional package signing

Not enabled by default (no cert in repo). When you have a code-signing certificate, sign after pack with `dotnet nuget sign` (see [docs/releasing.md](docs/releasing.md)).

### Product description (keep in sync)

| | Text |
|--|------|
| **Short** (NuGet / `.csproj` / GitHub About) | Domain-based caching for ASP.NET Core that orchestrates Output Cache, FusionCache, and client Cache-Control under the same model. |
| **Lead** (README intro) | **CacheOrchestrator** is domain-based caching for ASP.NET Core: define rules once per domain in configuration, then apply them on endpoints with a single attribute or extension. It orchestrates Output Cache, FusionCache, and client Cache-Control under the same model. |

Core package `Description` may append: `Redis backends: install CacheOrchestrator.Redis.`

## Community expectations

- Be respectful in issues and pull requests.
- No harassment, spam, or personal attacks.
- The maintainer may close issues/PRs or block accounts that make collaboration impossible.

## Pull requests

1. **Fork** (or branch from `main` if you have write access)
2. Keep PRs focused — one topic per PR when possible
3. Ensure `dotnet build` and unit tests pass
4. Prefer clear commit messages (what / why, not just “fix”)
5. Fill in the PR description: problem, approach, test plan
6. Do **not** commit secrets, production Redis endpoints, or local `_local/` artifacts

### Safe change checklist

1. Build solution (`CacheOrchestrator.slnx`)
2. Run unit tests
3. Update sample if public API or config surface changes
4. Avoid reintroducing a separate Abstractions assembly
5. Avoid non-English comments or `ct` as a public parameter name

## Issues

- Bugs: use the bug report template; include TFM, package version, minimal repro
- Features: describe the domain/use case and whether it is core vs Redis package scope

## Security

Please do **not** open public issues for vulnerabilities. See [SECURITY.md](SECURITY.md).

## License

By contributing, you agree that your contributions are licensed under the same [MIT License](LICENSE.md) as the project.
