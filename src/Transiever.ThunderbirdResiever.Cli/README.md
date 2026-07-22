# `tbrx`

`tbrx` migrates one Thunderbird IMAP account's supported filters to server-side Sieve.

```bash
tbrx export [--profile <directory> | --filters <file>]
tbrx run [--profile <directory> | --filters <file>]
tbrx rollback
```

`run` exports in memory, optionally optimizes, previews the server candidate, and asks before deployment.
It writes no local files unless `--write-artifacts` is present.
`rollback` restores the newest inactive SieveRuler backup and never reads Thunderbird data.

Use `--allow-partial` only after reviewing diagnostics when an enabled filter was skipped.
Unattended partial runs require it.

ManageSieve configuration uses `TRANSIEVER_SIEVE_HOST`, `TRANSIEVER_SIEVE_PORT`, `TRANSIEVER_SIEVE_USERNAME`, `TRANSIEVER_SIEVE_PASSWORD`, and `TRANSIEVER_SIEVE_SECURITY_MODE`.
The corresponding `--sieve-*` options override them for one command.

Shared `olrx` workflow options are retained: optimization flags, `--dry-run`, `--deploy`, compatible-rule adoption, history retention, optional artifact paths, and `--write-artifacts`.

See [the compatibility guide](../../docs/thunderbird-export.md) before deploying the beta.
