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
| NuGet `CacheOrchestrator` | [src/CacheOrchestrator/README.md](../../src/CacheOrchestrator/README.md) |
| NuGet `CacheOrchestrator.Redis` | [src/CacheOrchestrator.Redis/README.md](../../src/CacheOrchestrator.Redis/README.md) |
| NuGet `CacheOrchestrator.HttpBus` | [src/CacheOrchestrator.HttpBus/README.md](../../src/CacheOrchestrator.HttpBus/README.md) |
| NuGet `CacheOrchestrator.EFCore.Invalidation` | [src/CacheOrchestrator.EFCore.Invalidation/README.md](../../src/CacheOrchestrator.EFCore.Invalidation/README.md) |

Do **not** pack the root README into the core package (HTML/logo does not render well on nuget.org).

## Checklist

1. Merge all release work to `main`.
2. Update **CHANGELOG.md** and **PACKAGE_RELEASE_NOTES.md**.
3. Commit on `main`; wait for **Build and Test** to pass.
4. Create an **annotated tag**:

   ```bash
   git tag -a v1.0.0 -m "CacheOrchestrator 1.0.0"
   git push origin v1.0.0
   ```

5. Create a **GitHub Release** for that tag (**not** marked pre-release for stable 1.0.0).  
   This triggers [`.github/workflows/publish.yml`](../../.github/workflows/publish.yml):
   - test (core + Redis / Bus / EFCore unit tests + integration on net8/net10 + Testcontainers Redis)
   - `dotnet pack` → `.nupkg` + `.snupkg`
   - **NuGet Trusted Publishing** (OIDC via `NuGet/login@v1`)
   - **Admin Console App Docker image** → `ghcr.io/amarinsek/cacheorchestrator-admin-console` (same version tags)

6. Confirm nuget.org for **CacheOrchestrator**, **.Redis**, **.HttpBus**, and **.EFCore.Invalidation**; optionally **unlist** old pre-release versions.  
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

```bash
dotnet pack src/CacheOrchestrator/CacheOrchestrator.csproj -c Release -o ./nupkg
dotnet pack src/CacheOrchestrator.Redis/CacheOrchestrator.Redis.csproj -c Release -o ./nupkg
dotnet pack src/CacheOrchestrator.HttpBus/CacheOrchestrator.HttpBus.csproj -c Release -o ./nupkg
dotnet pack src/CacheOrchestrator.EFCore.Invalidation/CacheOrchestrator.EFCore.Invalidation.csproj -c Release -o ./nupkg
```
