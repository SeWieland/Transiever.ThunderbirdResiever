# Transiever.ThunderbirdResiever

Migrate supported Thunderbird message filters to server-side Sieve without installing or starting Thunderbird.

```text
Thunderbird msgFilterRules.dat
    -> tbrx / ThunderbirdResiever
        -> SieveRuler
            -> ManageSieve
```

> [!WARNING]
> The first release is an unstable beta.
> Testers are wanted, especially for real Windows, Linux, Flatpak, and Snap profile layouts.
> Please report redacted results in [GitHub Issues](https://github.com/SeWieland/Transiever.ThunderbirdResiever/issues).

`tbrx` reads one Thunderbird account per invocation and never modifies Thunderbird data.
`export` supports conservative local export for complete POP sources; `run` remains IMAP-only for deployment.
It can discover standard profiles or use an explicit profile/filter file.

```bash
tbrx export --profile /path/to/profile
tbrx run --filters /path/to/msgFilterRules.dat --optimize balanced
tbrx rollback
```

Run `tbrx` without arguments for help.
That path performs no profile, file, credential, or network access.

## Beta scope

- Windows x64 and Linux x64.
- Thunderbird filter format version 9 in strict UTF-8.
- Resolved IMAP accounts only for `run`.
- Conservative whole-rule export into SieveRuler schema v1.
- Fileless preview/deployment by default with server-side backup and rollback.
- Native AOT single-file executables.

See the [CLI guide](src/Transiever.ThunderbirdResiever.Cli/README.md), [export and compatibility guide](docs/thunderbird-export.md), [adapter guide](src/Transiever.ThunderbirdResiever/README.md), and [architecture](docs/architecture.md).

## Development

```bash
dotnet build Transiever.ThunderbirdResiever.slnx
dotnet test Transiever.ThunderbirdResiever.slnx
dotnet run --project src/Transiever.ThunderbirdResiever.Cli -- --help
```

No real Thunderbird files are committed.
Tests create synthetic profiles in temporary directories.

## AI usage

Transiever is a personal hobby project created to solve practical problems I have encountered myself.

AI is used heavily throughout its development. It supports research, design exploration, implementation, debugging, and documentation.

The project is developed using test-driven development and reviewed by a human, me. Its direction, behavior, and quality remain guided by the problems it is intended to solve.
