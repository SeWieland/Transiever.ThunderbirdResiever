# ThunderbirdResiever adapter

This library discovers Thunderbird account filter files, reads the narrow account preferences needed to identify them, parses version-9 filters, and maps the supported subset to SieveRuler rules.

It is non-packaged and read-only.
Unsupported enabled rules return diagnostics instead of partial approximations.

The complete compatibility and safety contract lives in the [Thunderbird export and compatibility guide](../../docs/thunderbird-export.md).
