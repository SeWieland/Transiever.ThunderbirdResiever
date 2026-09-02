# Thunderbird export and compatibility

`tbrx` reads Thunderbird data only.
It never edits, copies, normalizes, or exclusively locks a profile file.

Profile discovery checks exactly these roots:

- `%APPDATA%\Thunderbird` on Windows.
- `~/.thunderbird` on standard Linux installations.
- `~/.var/app/org.mozilla.Thunderbird/.thunderbird` for Flatpak.
- `~/snap/thunderbird/common/.thunderbird` for Snap.

Use `--profile <directory>` as the explicit profile fallback for a relocated profile.
Use `--filters <msgFilterRules.dat>` as the exact-filter fallback when one account must be selected or unrelated profile entries are incomplete.
Discovery reads `profiles.ini` and only the `mail.account.*` and `mail.server.*` string preferences needed from `prefs.js`.
It is not a general JavaScript parser.
Profile and filter paths use native comparison semantics: Windows comparisons are case-insensitive, while Linux comparisons are case-sensitive.
Paths are compared lexically without resolving filesystem links, so Linux symlink or bind-mount aliases remain distinct path spellings.

Filters are per account.
If exactly one source is eligible, `tbrx` selects it.
Interactive runs prompt when several exist; redirected input must specify `--filters`.
Only an account whose hostname, username, server type, and local directory resolve from `prefs.js` may enter source selection.
Incomplete discovery is refused before export, credentials, or network access; an exact filter still must map completely to one account.
`export` may produce a conservative local result for a complete POP source, but `run` is IMAP-only and rejects POP before configuration or deployment.

The parser accepts strict UTF-8 `msgFilterRules.dat` format version 9 and Mozilla's line-oriented quoted attribute format.
Malformed files and unknown file versions fail.
Unsupported semantics produce diagnostics and skip the entire enabled rule.
Disabled rules are ignored.

## Compatibility matrix

| Rule behavior | `olrx` | Initial `tbrx` |
| --- | --- | --- |
| Sender contains | Yes | `From` + `contains` |
| Recipient contains | Yes | `To or Cc` + `contains`; separate To/Cc deferred |
| Subject/body contains | Yes | Yes |
| Subject-or-body | Yes | Equivalent flat OR terms |
| Has attachment | Yes | `has attachment status is true` |
| Exceptions | Yes | Negative `contains` in match-all rules with a positive condition |
| Move/copy | Yes | Same-account IMAP folder URI only |
| Redirect | Yes | No; Thunderbird Forward is not Sieve redirect |
| Mark read | Yes | Yes |
| Delete to Trash | Yes | Deferred until account deletion semantics can be proven |
| Stop processing | Yes | Yes |
| Match-all/manual/post-junk/custom rules | No equivalent | Diagnostic and skip |

Folder actions require an `imap://` URI whose host and user match the selected account.
The mailbox path is URI-decoded and retains Thunderbird's native casing; host and username comparisons ignore case and trim trailing host dots, but do not DNS-resolve or canonicalize aliases.
Cross-account, local, news, POP, and empty or ambiguous targets are skipped with their whole rule.

## Partial exports

`run` refuses zero exported rules.
When one or more enabled rules are skipped, an interactive run requires explicit acknowledgement.
Redirected input requires `--allow-partial`; otherwise it stops before credentials or network access.

Tester reports should include Thunderbird version, OS, package type, diagnostics, and only a minimal redacted fixture.
Do not attach a complete profile or private mailbox data.

The storage locations follow [Thunderbird profile guidance](https://support.mozilla.org/en-US/kb/profiles-where-thunderbird-stores-user-data) and [Linux installation guidance](https://support.mozilla.org/en-US/kb/installing-thunderbird-linux).
The parser follows Mozilla's [filter-list reader](https://searchfox.org/comm-central/source/mailnews/search/src/nsMsgFilterList.cpp) and [filter serializer/action table](https://searchfox.org/comm-central/source/mailnews/search/src/nsMsgFilter.cpp).
