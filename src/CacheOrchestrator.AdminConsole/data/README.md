# Operator data directory

Used when `AdminConsole:Hints` points at `data/` (default in **Production** / Docker).

| Path | Role |
|------|------|
| **`rules/*.json`** | Your custom hint packs (optional). Empty = product `hints/core-hints.json` only. |
| **`disabled.local.json`** | Written by the Settings UI (enable/disable). Created on first change. Do not commit. |

**Development** (`dotnet run` with `ASPNETCORE_ENVIRONMENT=Development`) still uses `hints/*.json` and `hints/disabled.local.json` — see [hints/README.md](../hints/README.md).

**Docker:** mount a host folder over `/app/data` so custom rules and disabled state survive restarts. Full guide: [deploy/admin/README.md](../../../deploy/admin/README.md). Orientation: [Guide — operations](../../../docs/guide/operations.md).
