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
The corresponding non-secret `--sieve-*` options override them for one command.
Use `--sieve-password-stdin` to read one non-empty password from redirected standard input.
The selector rejects interactive terminal input; omit it to use the non-echoed password prompt.
Passwords cannot be supplied as command-line values.

Select a saved account for `run` or `rollback` with `--account account:<guid>`;
`TRANSIEVER_ACCOUNT` supplies the default selector, and the command-line selector wins when both are present.
The selected profile supplies endpoint defaults and its stored password is used only when no safe explicit input or password environment variable was provided.
Command-line and environment values override profile fields.
Missing, unavailable, expired, or rejected stored passwords are never reused; an interactive terminal may prompt, while redirected input without `--sieve-password-stdin` requires a usable stored password or `TRANSIEVER_SIEVE_PASSWORD`.
The current ManageSieve connector supports the `PLAIN` authentication preference only; other saved preferences fail before credential lookup.
OAuth refresh tokens are not used as passwords.

Shared `olrx` workflow options are retained: optimization flags, `--dry-run`, `--deploy`, compatible-rule adoption, history retention, optional artifact paths, and `--write-artifacts`.

See [the compatibility guide](../../docs/thunderbird-export.md) before deploying the beta.
