# Releasing CacheOrchestrator

> **Reference.** Product overview: [root README](../../README.md). Orientation: [Guide](../guide/README.md). Catalog: [documentation index](../../README.md). Contributor procedure.

How versions, NuGet packages, and GitHub Releases fit together.

## Versioning (MinVer)

Package version is **not** hardcoded. [MinVer](https://github.com/adamralph/minver) reads **Git tags**.

| Situation | Resulting package version (typical) |
|-----------|-------------------------------------|
| Tag `v1.0.0` on the commit you build | `1.0.0` |
| Commits after `v1.0.0` without a new tag | `1.0.1-rc.0.N` |
| Tag `v1.0.1` | `1.0.1` |

Tag prefix is **`v`** (`MinVerTagPrefix` in `Directory.Build.props`).

```bash
dotnet build src/CacheOrchestrator/CacheOrchestrator.csproj -c Release -v:m
```

**Requires full Git history** on CI (`fetch-depth: 0` — already set in workflows).

## Release notes (two files)

| File | Audience | Role |
|------|----------|------|
| [CHANGELOG.md](../../CHANGELOG.md) | Humans / GitHub | Full history |
| [PACKAGE_RELEASE_NOTES.md](../../PACKAGE_RELEASE_NOTES.md) | nuget.org | Short notes for **this** version only |

## Package READMEs (NuGet vs GitHub)

| Surface | File |
|---------|------|
| GitHub (full story, logo) | Root [README.md](../../README.md) |
| Guide (orientation) | [docs/guide/README.md](../guide/README.md) |
| NuGet `CacheOrchestrator` (meta) | [src/CacheOrchestrator/README.md](../../src/CacheOrchestrator/README.md) |
| NuGet `CacheOrchestrator.Core` | [src/CacheOrchestrator.Core/README.md](../../src/CacheOrchestrator.Core/README.md) |
| NuGet `CacheOrchestrator.AspNetCore` | [src/CacheOrchestrator.AspNetCore/README.md](../../src/CacheOrchestrator.AspNetCore/README.md) |
| NuGet `CacheOrchestrator.FusionCache` | [src/CacheOrchestrator.FusionCache/README.md](../../src/CacheOrchestrator.FusionCache/README.md) |
| NuGet `CacheOrchestrator.HybridCache` | [src/CacheOrchestrator.HybridCache/README.md](../../src/CacheOrchestrator.HybridCache/README.md) |
| NuGet `CacheOrchestrator.Redis` (meta) | [src/CacheOrchestrator.Redis/README.md](../../src/CacheOrchestrator.Redis/README.md) |
| NuGet `CacheOrchestrator.AspNetCore.Redis` | [src/CacheOrchestrator.AspNetCore.Redis/README.md](../../src/CacheOrchestrator.AspNetCore.Redis/README.md) |
| NuGet `CacheOrchestrator.FusionCache.Redis` | [src/CacheOrchestrator.FusionCache.Redis/README.md](../../src/CacheOrchestrator.FusionCache.Redis/README.md) |
| NuGet `CacheOrchestrator.Redis.Shared` (support) | [src/CacheOrchestrator.Redis.Shared/README.md](../../src/CacheOrchestrator.Redis.Shared/README.md) — transitive only; do not promote as install target |
| NuGet `CacheOrchestrator.HttpBus` | [src/CacheOrchestrator.HttpBus/README.md](../../src/CacheOrchestrator.HttpBus/README.md) |
| NuGet `CacheOrchestrator.EFCore.Invalidation` | [src/CacheOrchestrator.EFCore.Invalidation/README.md](../../src/CacheOrchestrator.EFCore.Invalidation/README.md) |

Do **not** pack the root README into library packages (HTML/logo does not render well on nuget.org). Admin Console App is **not** a NuGet package (Docker / GHCR only). `Redis.Shared` is published so leaf/meta packages restore; apps should not reference it directly.

## Checklist

1. Merge all release work to `main`.
2. Update **CHANGELOG.md** and **PACKAGE_RELEASE_NOTES.md**.
3. Commit on `main`; wait for **Build and Test** to pass.
4. Create an **annotated tag**:

   ```bash
   git tag -a v1.0.0 -m "CacheOrchestrator 1.0.0"
   git push origin v1.0.0
   ```

5. Create a **GitHub Release** for that tag (**not** marked pre-release for a stable release).  
   This triggers [`.github/workflows/publish.yml`](../../.github/workflows/publish.yml):
   - unit tests (Core / Fusion / Hybrid / AspNetCore / Redis.Shared / AspNetCore.Redis / FusionCache.Redis / Redis meta / HttpBus / EF) on net8 + net10; Admin Console on net10
   - integration tests on net8/net10 + Testcontainers Redis; Minimal sample smoke
   - `dotnet pack` for **all eleven** NuGet libraries → `.nupkg` + `.snupkg` (includes Redis.Shared as support)
   - **NuGet Trusted Publishing** (OIDC via `NuGet/login@v1`)
   - **Admin Console App Docker image** → `ghcr.io/amarinsek/cacheorchestrator-admin-console` (same version tags)

6. Confirm nuget.org for all eleven packages (meta + Core + AspNetCore + FusionCache + HybridCache + Redis.Shared + AspNetCore.Redis + FusionCache.Redis + Redis meta + HttpBus + EFCore.Invalidation); optionally **unlist** old pre-release versions.  
   Confirm GHCR package **cacheorchestrator-admin-console** (see [deploy/admin/README.md](../../deploy/admin/README.md)).  
   First-time: set the package **visibility** to Public if anonymous `docker pull` is desired.

### NuGet Trusted Publishing

1. nuget.org → **Trusted Publishing**
2. Policy: owner `amarinsek`, repo `CacheOrchestrator`, workflow **`publish.yml`**, **Environment empty**
3. GitHub secret **`NUGET_USER`** = nuget.org username (`amarinsek`)
4. First successful publish within 7 days fully activates a new policy

## Optional: package signing

Not enabled in CI. See historical notes: sign locally with `dotnet nuget sign` if you have a certificate.

## Local pack smoke test

Pack the eleven NuGet libraries (same set as `publish.yml`). Do **not** `dotnet pack` the whole solution — Benchmarks would produce an unwanted nupkg if packable.

```bash
mkdir -p nupkg
for proj in \
  src/CacheOrchestrator.Core/CacheOrchestrator.Core.csproj \
  src/CacheOrchestrator.AspNetCore/CacheOrchestrator.AspNetCore.csproj \
  src/CacheOrchestrator.FusionCache/CacheOrchestrator.FusionCache.csproj \
  src/CacheOrchestrator.HybridCache/CacheOrchestrator.HybridCache.csproj \
  src/CacheOrchestrator/CacheOrchestrator.csproj \
  src/CacheOrchestrator.Redis.Shared/CacheOrchestrator.Redis.Shared.csproj \
  src/CacheOrchestrator.AspNetCore.Redis/CacheOrchestrator.AspNetCore.Redis.csproj \
  src/CacheOrchestrator.FusionCache.Redis/CacheOrchestrator.FusionCache.Redis.csproj \
  src/CacheOrchestrator.Redis/CacheOrchestrator.Redis.csproj \
  src/CacheOrchestrator.HttpBus/CacheOrchestrator.HttpBus.csproj \
  src/CacheOrchestrator.EFCore.Invalidation/CacheOrchestrator.EFCore.Invalidation.csproj
do
  dotnet pack "$proj" -c Release -o ./nupkg
done
ls nupkg
```
