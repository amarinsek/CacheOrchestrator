# Releasing CacheOrchestrator

> **Reference.** Product overview: [root README](../../README.md). Orientation: [Guide](../guide/README.md). Catalog: [documentation index](../../README.md). Contributor procedure.

How versions, NuGet packages, and GitHub Releases fit together.

## Table of Contents

- [Versioning (MinVer)](#versioning-minver)
- [Release notes](#release-notes)
- [Package READMEs (NuGet vs GitHub)](#package-readmes-nuget-vs-github)
- [Compatibility and package gates](#compatibility-and-package-gates)
- [Checklist](#checklist)
- [Optional: package signing](#optional-package-signing)
- [Local pack smoke test](#local-pack-smoke-test)

## Versioning (MinVer)

Package version is **not** hardcoded. [MinVer](https://github.com/adamralph/minver) reads **Git tags**.

| Situation | Resulting package version (typical) |
|-----------|-------------------------------------|
| Tag `v3.0.0` on the commit you build | `3.0.0` |
| Commits after `v3.0.0` without a new tag | `3.0.1-rc.0.N` |
| Tag `v3.0.1` | `3.0.1` |

Tag prefix is **`v`** (`MinVerTagPrefix` in `Directory.Build.props`).

```bash
dotnet build src/CacheOrchestrator/CacheOrchestrator.csproj -c Release -v:m
```

**Requires full Git history** on CI (`fetch-depth: 0` — already set in workflows).

## Release notes

| Surface | Audience | Role |
|---------|----------|------|
| [GitHub Releases](https://github.com/amarinsek/CacheOrchestrator/releases) | Humans | Full history per tag (canonical) |
| [PACKAGE_RELEASE_NOTES.md](../../PACKAGE_RELEASE_NOTES.md) | nuget.org | Short notes for **this** version only; link to the matching `releases/tag/v…` |

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

## Compatibility and package gates

Three separate gates cover different failure modes. A successful `dotnet build` does not replace a package validation run, and analyzer unit tests do not prove that the analyzer reached a NuGet consumer.

### Public API analyzer (build time)

All packable libraries are listed in `_PublicApiProjectNames` in [`Directory.Build.props`](../../Directory.Build.props). They use `Microsoft.CodeAnalysis.PublicApiAnalyzers` and keep their contracts under:

```text
eng/PublicApi/{Project}/PublicAPI.Shipped.txt
eng/PublicApi/{Project}/PublicAPI.Unshipped.txt
```

This gate runs during a normal build:

```bash
dotnet build CacheOrchestrator.slnx -c Release
```

Add reviewed API additions to `PublicAPI.Unshipped.txt`. Record removals and changed signatures with the analyzer-generated `*REMOVED*` entries only after making an explicit breaking-change decision. Maintainers promote the accepted release surface to `PublicAPI.Shipped.txt` when establishing a release baseline. Do not suppress the analyzer merely to make a build pass.

### NuGet package validation (pack time)

The same public-project list enables the .NET SDK package validator with:

```xml
<EnablePackageValidation>true</EnablePackageValidation>
<EnableStrictModeForCompatibleTfms>true</EnableStrictModeForCompatibleTfms>
```

It runs as part of `dotnet pack`, validates the produced package assets, and strictly checks that compatible target frameworks expose compatible APIs. For this repository that includes the `net8.0` / `net10.0` relationship. Run a Release build first, then pack with `--no-build`:

```bash
dotnet restore CacheOrchestrator.slnx
dotnet build CacheOrchestrator.slnx -c Release --no-restore
dotnet pack src/CacheOrchestrator.Core/CacheOrchestrator.Core.csproj \
  -c Release --no-build -o ./nupkg
```

The repository does not set `PackageValidationBaselineVersion`, so this gate does **not** compare packages against nuget.org by default. To compare a release against a chosen baseline version:

```bash
dotnet pack src/CacheOrchestrator.Core/CacheOrchestrator.Core.csproj \
  -c Release --no-build -o ./nupkg \
  -p:PackageValidationBaselineVersion=3.0.0
```

Use the latest applicable stable version from the same major line. Baseline validation may restore that package from configured NuGet sources and therefore requires network access when it is not already cached. Intentional major-version breaks belong in the public API baseline and release notes, not in blanket package-validation suppressions.

### Packaged analyzer consumer smoke

Analyzer unit tests verify `COIDENTITY001` logic. The **Package analyzer consumer smoke** step in [`.github/workflows/build.yml`](../../.github/workflows/build.yml) additionally verifies delivery from the generated packages on every pull request to `main` and every push to `main`:

1. Pack `CacheOrchestrator.Core`, `CacheOrchestrator.FusionCache`, `CacheOrchestrator.AspNetCore`, and the `CacheOrchestrator` meta package.
2. Confirm that the `CacheOrchestrator.AspNetCore` nupkg contains `analyzers/dotnet/cs/CacheOrchestrator.Analyzers.dll`.
3. Create an external `net8.0` project with only the meta package installed.
4. Compile an action with duplicate `GET` identity bindings.
5. Require the consumer build to fail with `COIDENTITY001` from the packaged analyzer.

The analyzer is physically packed once in `CacheOrchestrator.AspNetCore`; meta-package consumers receive it transitively. Do not also embed it in the meta package, because that executes the same analyzer twice and duplicates diagnostics.

The release publish workflow packs the final package set and therefore runs SDK package validation. It does not repeat the external analyzer consumer project; the release checklist requires the commit on `main` to pass **Build and Test** before tagging.

## Checklist

1. Merge all release work to `main`.
2. Update **PACKAGE_RELEASE_NOTES.md** (short NuGet blurb + link to the release tag you are about to create). Draft the **GitHub Release** body from merged PR worklogs.
3. Commit on `main`; wait for **Build and Test** to pass, including the packaged-analyzer consumer smoke.
4. Create an **annotated tag**:

   ```bash
   git tag -a v3.0.0 -m "CacheOrchestrator 3.0.0"
   git push origin v3.0.0
   ```

5. Create a **GitHub Release** for that tag (**not** marked pre-release for a stable release).  
   This triggers [`.github/workflows/publish.yml`](../../.github/workflows/publish.yml):
   - unit tests (`Core` / `FusionCache` / `HybridCache` / `AspNetCore` / `Redis.Shared` / `AspNetCore.Redis` / `FusionCache.Redis` / Redis meta / `HttpBus` / EF) on net8 + net10; Admin Console App on net10
   - integration tests on net8/net10 + Testcontainers Redis; Minimal sample smoke
   - `dotnet pack` for **all** packable NuGet libraries → `.nupkg` + `.snupkg` (includes Redis.Shared as support; see pack list in `publish.yml`); each pack runs SDK package validation
   - **NuGet Trusted Publishing** (OIDC via `NuGet/login@v1`)
   - **Admin Console App Docker image** → `ghcr.io/amarinsek/cacheorchestrator-admin-console` (same version tags)

6. Confirm nuget.org for **all** packages produced by `publish.yml` (including support `Redis.Shared`); optionally **unlist** old pre-release versions.  
   Confirm GHCR package **cacheorchestrator-admin-console** (see [deploy/admin/README.md](../../deploy/admin/README.md)).  
   First-time: set the package **visibility** to Public if anonymous `docker pull` is desired.

### NuGet Trusted Publishing

1. nuget.org → **Trusted Publishing**
2. Policy: owner `amarinsek`, repo `CacheOrchestrator`, workflow **`publish.yml`**, **Environment empty**
3. GitHub secret **`NUGET_USER`** = nuget.org username (`amarinsek`)
4. First successful publish within 7 days fully activates a new policy

## Optional: package signing

Not enabled in CI. Sign locally with `dotnet nuget sign` if you have a certificate.

## Local pack smoke test

Pack **all** NuGet libraries listed in `publish.yml`. Do **not** `dotnet pack` the whole solution — Benchmarks would produce an unwanted nupkg if packable.

```bash
dotnet restore CacheOrchestrator.slnx
dotnet build CacheOrchestrator.slnx -c Release --no-restore

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
  dotnet pack "$proj" -c Release --no-build -o ./nupkg
done
ls nupkg
```

Expect 11 `.nupkg` and 11 `.snupkg` files. Any package-validation diagnostic fails the corresponding pack and must be reviewed before release.

For the analyzer delivery smoke, use the generated package set as a local NuGet source and reproduce the consumer from `build.yml`. The consumer build is expected to fail; success is a smoke-test failure:

```bash
unzip -l nupkg/CacheOrchestrator.AspNetCore.*.nupkg | \
  grep "analyzers/dotnet/cs/CacheOrchestrator.Analyzers.dll"

consumer_dir=$(mktemp -d)
dotnet new classlib --framework net8.0 --name PackageConsumer --output "$consumer_dir"
dotnet add "$consumer_dir/PackageConsumer.csproj" package CacheOrchestrator \
  --source "$PWD/nupkg" --prerelease

cat > "$consumer_dir/Class1.cs" <<'EOF'
using CacheOrchestrator.Identity;

namespace PackageConsumer;

public sealed class DuplicateIdentity
{
    [CacheIdentity(new[] { "GET" }, "first")]
    [CacheIdentity(new[] { "GET" }, "second")]
    public void Execute() { }
}
EOF

set +e
dotnet build "$consumer_dir/PackageConsumer.csproj" > "$consumer_dir/build.log" 2>&1
consumer_exit=$?
set -e
if [ "$consumer_exit" -eq 0 ]; then
  cat "$consumer_dir/build.log"
  echo "Expected COIDENTITY001 from the packaged analyzer."
  exit 1
fi
if ! grep -q "COIDENTITY001" "$consumer_dir/build.log"; then
  cat "$consumer_dir/build.log"
  echo "Consumer build failed without COIDENTITY001."
  exit 1
fi
echo "Packaged analyzer emitted COIDENTITY001 as expected."
```

The final message confirms success. This external-project check is different from:

```bash
dotnet test tests/CacheOrchestrator.Analyzers.UnitTests/CacheOrchestrator.Analyzers.UnitTests.csproj -c Release
```

Run the unit tests for analyzer behavior and the package consumer smoke for analyzer delivery.
