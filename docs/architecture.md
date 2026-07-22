# Architecture

ThunderbirdResiever is an intentionally thin source adapter.

```text
profiles.ini + prefs.js + msgFilterRules.dat
    -> source discovery and conservative export
    -> SieveRuler schema-v1 RuleDocument
    -> SieveRuler preview, reconciliation, deployment, history, rollback
    -> ManageSieve
```

The adapter library is cross-platform, non-packaged, and read-only.
It owns no Sieve generation, deployment, provider, or JSON schema logic.

The production adapter owns the repository's only direct SieveRuler dependency.
The CLI and tests receive it transitively.
A conditional project reference supports the private umbrella checkout; standalone builds use the released NuGet package.

`ThunderbirdRuleSource`, `IThunderbirdRuleExporter`, export results/diagnostics, and `ThunderbirdExportApplication` are internal repository test seams rather than a promised public package API.
