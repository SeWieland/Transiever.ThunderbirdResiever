# AGENTS.md

## Project boundary

`Transiever.ThunderbirdResiever` is the cross-platform, read-only Thunderbird source adapter for `Transiever.SieveRuler`.
Its `tbrx` CLI discovers Thunderbird profiles, exports one IMAP account's supported version-9 filters, and runs the SieveRuler synchronization workflow.

Keep Thunderbird profile discovery, narrow `prefs.js` reading, `msgFilterRules.dat` parsing, source selection, and export diagnostics here.
SieveRuler owns rule models, JSON, optimization, Sieve processing, reconciliation, ManageSieve adaptation, deployment, backups, and rollback.

## Agent index

```text
Transiever.ThunderbirdResiever.slnx
docs/architecture.md
docs/thunderbird-export.md
src/
  Transiever.ThunderbirdResiever/
  Transiever.ThunderbirdResiever.Cli/
  Transiever.ThunderbirdResiever.UnitTest/
  Transiever.ThunderbirdResiever.Cli.UnitTest/
```

The adapter may reference a sibling SieveRuler project during umbrella development.
Standalone builds must fall back to the versioned `Transiever.SieveRuler` package.
No project may depend on files outside this repository in published CI.

## Validation

```bash
dotnet build Transiever.ThunderbirdResiever.slnx
dotnet test Transiever.ThunderbirdResiever.slnx
dotnet run --project src/Transiever.ThunderbirdResiever.Cli -- --help
dotnet build Transiever.ThunderbirdResiever.slnx -p:SieveRulerProject=__missing_sieveruler__.csproj
dotnet test Transiever.ThunderbirdResiever.slnx -p:SieveRulerProject=__missing_sieveruler__.csproj
```

Tests use synthetic profiles only.
They must not require Thunderbird, credentials, a configured account, or a live provider.

## Non-negotiables

- Never write, normalize, copy, or take an exclusive lock on Thunderbird profile data.
- Read filter files as stable snapshots and fail if their metadata changes during the read.
- Parse only strict UTF-8 filter format version 9.
- Skip an entire enabled rule when any condition, action, context, or folder target cannot be mapped without changing meaning.
- Never deploy an empty export.
- Require acknowledgement for partial interactive runs and `--allow-partial` for partial unattended runs.
- Use a deterministic per-account source ID and migrate one account per invocation.
- Keep SieveRuler schema v1 unchanged.
- Keep only `run`, `export`, and `rollback` in `tbrx`.
- Running without arguments must display help without reading profiles, files, credentials, or network state.
- Keep the adapter and CLI Native AOT compatible; do not use reflection, `dynamic`, or runtime JSON metadata.

GitHub Actions are repository-local.
Releases are manual, stable from `main`, and beta from `dev`.
The initial release contains `win-x64` and `linux-x64` single-file Native AOT archives only unless a matching runner records an explicit RID-specific blocker.
