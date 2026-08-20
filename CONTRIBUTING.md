# Contributing to CacheOrchestrator

Thanks for helping improve **CacheOrchestrator**. This guide covers build, test, coding style, and pull requests.

## Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/download) that supports **net8.0** and **net10.0** (SDK 10.x is fine for multi-target builds). Libraries and library tests multi-target both; **Admin Console App** and its unit tests are **net10.0 only**; samples are net10-only.
- Optional: Docker (Redis integration tests, [playground topology labs](#topology-labs-docker-compose))

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

### Host (no Docker)

```bash
# One-minute InMemory demo (MISS → HIT)
dotnet run --project samples/CacheOrchestrator.Minimal

# Interactive playground (TTL, schedule, Redis, CRUD)
dotnet run --project samples/CacheOrchestrator.Sample

# Same check as CI (miss → hit on /hello; needs prior Release build)
dotnet build samples/CacheOrchestrator.Minimal -c Release
bash samples/CacheOrchestrator.Minimal/smoke.sh
```

Notes: [samples/CacheOrchestrator.Minimal](samples/CacheOrchestrator.Minimal), [samples/CacheOrchestrator.Sample](samples/CacheOrchestrator.Sample).

### Topology labs (Docker Compose)

Numbered Compose stacks for **cache layouts**: Playground + Prometheus + Admin Console, then Redis L2, two app instances, HTTP cluster bus, dual Redis. They are a **teaching environment**, not a production blueprint, and **not** part of the NuGet packages.

Docker must be running. From the repository root:

```bash
docker compose -f samples/CacheOrchestrator.Sample/labs/compose/01-observability.yml up --build
```

| Stage | Compose file | Stack |
|-------|----------------|--------|
| **01** | `compose/01-observability.yml` | Playground + Prometheus + Admin Console (InMemory) |
| **02** | `compose/02-redis.yml` | Stage 01 + Redis as Fusion **L2** |
| **03** | `compose/03-multi.yml` | Two playgrounds + shared Redis L2 |
| **04** | `compose/04-bus.yml` | Stage 03 + HTTP cluster bus |
| **05** | `compose/05-dual-redis-bus.yml` | Two Redis (OC store vs Fusion L2/backplane) + bus |

Default URLs: Playground [http://localhost:5289](http://localhost:5289), Admin Console [http://localhost:5188](http://localhost:5188). Stages 03–05 also publish Playground B on port **5290**.

After library or Admin Console changes, rebuild images (`up --build`) so the stacks pick up new assemblies.

Full guide, diagrams, troubleshooting: [samples/CacheOrchestrator.Sample/labs/README.md](samples/CacheOrchestrator.Sample/labs/README.md). Orientation: [Guide — topologies](docs/guide/topologies.md).

If you change public cache behaviour, check whether a lab stage still demonstrates what it claims (especially OC vs Fusion vs bus gaps in stages 03–05).

## Project layout

| Path | Role |
|------|------|
| `src/CacheOrchestrator` | Core library (InMemory; no Redis package references) |
| `src/CacheOrchestrator.Redis` | Optional Redis backends |
| `src/CacheOrchestrator.Bus` | Optional HTTP cluster command bus |
| `src/CacheOrchestrator.EFCore.Invalidation` | Optional SaveChanges invalidation |
| `src/CacheOrchestrator.AdminConsole` | Admin Console App (not a NuGet package; net10 only) |
| `deploy/admin` | Admin Console Docker runbook and example config |
| `tests/*` | Unit, integration, benchmarks |
| `samples/*` | Minimal demo, playground, [topology labs](samples/CacheOrchestrator.Sample/labs) |
| `docs/*` | Human docs (product README, [guide](docs/guide/README.md), reference) |
| `docs/templates/` | Copy-paste contributor files ([worklog](docs/templates/worklog-template.md)) |

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

When you change public API, config keys, or behaviour, update the **right tier** (do not dump reference into the root README):

1. **Product** — root [README.md](README.md) only if the try/minimal path or feature list changes
2. **Guide** — [docs/guide/](docs/guide/README.md) plus [getting-started](docs/getting-started.md) / [FAQ](docs/faq.md) / [domain-profiles](docs/domain-profiles.md) when orientation changes
3. **Reference** — the topic page under `docs/` (configuration, keys, deployment, …)
4. Record user-facing changes in the [worklog Changelog](#worklog) — **do not** edit [CHANGELOG.md](CHANGELOG.md) in the PR

Hub: [docs/README.md](docs/README.md).

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
Full release procedure (maintainer): **[docs/releasing.md](docs/releasing.md)**.

#### Release checklist (maintainer)

The maintainer folds each merged PR’s worklog Changelog into [CHANGELOG.md](CHANGELOG.md) (ongoing). On a release:

1. Confirm **CHANGELOG.md** and update **PACKAGE_RELEASE_NOTES.md**.
2. Commit on `main`.
3. Tag **`v{version}`** (e.g. `v1.0.0`) and push the tag.
4. Publish **GitHub Release** for that tag → `publish.yml` packs and pushes to NuGet and builds the Admin Console image on GHCR
   (Trusted Publishing / OIDC — configure policy on nuget.org; no `NUGET_API_KEY` secret required).
5. Confirm nuget.org (version from tag, release notes, symbols).

Do **not** set `<Version>` manually in `Directory.Build.props` — MinVer owns it.

All four NuGet packages share the same version and `PACKAGE_RELEASE_NOTES.md`.

#### Optional package signing

Not enabled by default (no cert in repo). When you have a code-signing certificate, sign after pack with `dotnet nuget sign` (see [docs/releasing.md](docs/releasing.md)).

### Product description (keep in sync)

| | Text |
|--|------|
| **Short** (NuGet / `.csproj` / GitHub About) | Domain-based configuration and coordination for ASP.NET Core Output Cache, FusionCache, and client Cache-Control — not a cache of its own. |
| **Lead** (README intro) | **CacheOrchestrator** configures and coordinates three existing layers in ASP.NET Core — Output Cache (OC), FusionCache (L1/L2), and client Cache-Control (CC) — under one **domain** model. Define the rules once in configuration, then apply them on endpoints with a single attribute or extension. It does not replace those systems or own a store: ASP.NET still holds the HTTP response, FusionCache still holds the object, and the browser or CDN still honours `Cache-Control`. |

Core package `Description` may append: `Redis backends: install CacheOrchestrator.Redis.`

## Worklog

Use a **worklog** for any branch that is more than a one-line fix. It is a **PR appendix**, not a file in the repository: the living record of the branch, then the archive on the pull request.

1. Copy [docs/templates/worklog-template.md](docs/templates/worklog-template.md) when you open the branch.
2. Fill metadata immediately (date, author, branch, issues, optional plan).
3. Update **Summary**, **Changelog**, and **Work items** as you work. Keep the filled copy outside the tree (draft, gist, or local notes) — do not add it to the commit.
4. Changelog records **net** changes only (no intermediate attempts of the same feature). List breaking changes explicitly.
5. Write **Work items** (and Changelog) for a future reader: what landed, and any rule that still applies. Do **not** record chat, rejected alternatives (“not X, because…”), or draft locations — those are meaningless outside the discussion.
6. When you open the PR:
   - **Summary** → GitHub title and the short description
   - The rest of the worklog (Changelog, Breaking changes, Work items) → the PR body as the archive

Do **not** edit [CHANGELOG.md](CHANGELOG.md) in a contributor PR. The worklog Changelog is the input; the maintainer copies net entries into `CHANGELOG.md` after merge (and into `PACKAGE_RELEASE_NOTES.md` when cutting a release).

## Community expectations

- Be respectful in issues and pull requests.
- No harassment, spam, or personal attacks.
- The maintainer may close issues/PRs or block accounts that make collaboration impossible.

## Pull requests

1. **Fork** (or branch from `main` if you have write access)
2. Copy the [worklog template](#worklog) for the branch
3. Keep PRs focused — one topic per PR when possible
4. Ensure `dotnet build` and unit tests pass (core `CacheOrchestrator.UnitTests` plus Redis / Bus / EFCore.Invalidation unit-test projects when those packages change; Admin Console tests on net10)
5. Prefer clear commit messages (what / why, not just “fix”)
6. Fill in the PR title and description from the worklog **Summary**; attach the rest of the worklog in the PR body ([Worklog](#worklog))
7. Do **not** commit secrets, production Redis endpoints, or filled worklogs

### Safe change checklist

1. Build solution (`CacheOrchestrator.slnx`)
2. Run unit tests (projects listed under [Tests](#tests))
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
