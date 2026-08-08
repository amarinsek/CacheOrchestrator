# Releasing CacheOrchestrator

How versions, NuGet packages, and GitHub Releases fit together.

## Versioning (MinVer)

Package version is **not** hardcoded. [MinVer](https://github.com/adamralph/minver) reads **Git tags**.

| Situation | Resulting package version (typical) |
|-----------|-------------------------------------|
| Tag `v1.0.0-rc.1` on the commit you build | `1.0.0-rc.1` |
| No `v*` tag yet, on main after N commits | `1.0.0-rc.0.N` (minimum major.minor = 1.0) |
| Tag `v1.0.0` | `1.0.0` |

Tag prefix is **`v`** (`MinVerTagPrefix` in `Directory.Build.props`).

```bash
# Inspect calculated version (after restore)
dotnet build src/CacheOrchestrator/CacheOrchestrator.csproj -c Release -v:m
# Or:
dotnet minver
```

**Requires full Git history** on CI (`fetch-depth: 0` — already set in workflows).

## Release notes (two files)

| File | Audience | Role |
|------|----------|------|
| [CHANGELOG.md](../CHANGELOG.md) | Humans / GitHub | Full history |
| [PACKAGE_RELEASE_NOTES.md](../PACKAGE_RELEASE_NOTES.md) | nuget.org | Short notes for **this** version only (`PackageReleaseNotes`) |

## Checklist

1. Merge all release work to `main`.
2. Update **CHANGELOG.md** (`[Unreleased]` → `[x.y.z]` section).
3. Rewrite **PACKAGE_RELEASE_NOTES.md** for this version (first RC: “Initial public release candidate…”).
4. Commit on `main`.
5. Create an **annotated tag** matching MinVer:

   ```bash
   git tag -a v1.0.0-rc.1 -m "CacheOrchestrator 1.0.0-rc.1"
   git push origin v1.0.0-rc.1
   ```

6. Create a **GitHub Release** for that tag (UI or `gh release create`).  
   This triggers [`.github/workflows/publish.yml`](../.github/workflows/publish.yml):
   - test (unit net8/net10, integration + Testcontainers Redis)
   - `dotnet pack` → `.nupkg` + `.snupkg`
   - **NuGet Trusted Publishing** (OIDC via `NuGet/login@v1` — no long-lived API key in GitHub Secrets)

### NuGet Trusted Publishing (what you configure on nuget.org)

1. nuget.org → username → **Trusted Publishing** (not classic API Keys).
2. Policy roughly:
   - Repository Owner: `amarinsek` (GitHub user/org)
   - Repository: `CacheOrchestrator`
   - Workflow file: **`publish.yml`** only (not the full path)
   - **Environment: leave empty** unless the workflow job sets `environment: …` (must match exactly)
3. First successful publish within **7 days** “locks” the policy permanently (“Use within 7 day(s)…”).
4. There is **no** secret string to copy into GitHub for this mode.

7. Confirm on nuget.org: version, release notes, symbols.

## Optional: package signing

Signing is **not** required for open-source nuget.org publishing and is **not** enabled in
`publish.yml` (GitHub Actions does not allow `if: secrets.*` conditions, and most OSS packages
ship unsigned).

If you later obtain a code-signing certificate, sign **locally** after pack (or add a dedicated
workflow step that always runs only when you intentionally enable it via a repository **variable**,
not via `if: secrets…`):

```bash
dotnet nuget sign ./nupkg/*.nupkg \
  --certificate-path ./cert.pfx \
  --certificate-password "$CERT_PASSWORD" \
  --timestamper http://timestamp.digicert.com

dotnet nuget sign ./nupkg/*.snupkg \
  --certificate-path ./cert.pfx \
  --certificate-password "$CERT_PASSWORD" \
  --timestamper http://timestamp.digicert.com
```

Never commit certificate files or passwords.

## Local pack smoke test

```bash
dotnet pack src/CacheOrchestrator/CacheOrchestrator.csproj -c Release -o ./nupkg
dotnet pack src/CacheOrchestrator.Redis/CacheOrchestrator.Redis.csproj -c Release -o ./nupkg
# Expect matching Version in both .nupkg names + .snupkg + <releaseNotes> in nuspec
```
