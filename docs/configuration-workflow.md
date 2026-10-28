# Configuration and command workflow

[← SchemaAlign](../README.md#documentation)

SchemaAlign separates project conventions from choices made for a single comparison. The
wizard guides interactive work, while commands provide the same operations for automation.

## Where settings belong

| Location | Responsibility |
| --- | --- |
| .schemaalign.json | Schema locations, shared table prefixes, and C# generation conventions. |
| Wizard | Comparison scope, change selection, preview, and application decisions. |
| CLI | Explicit schema overrides, comparison scope, output destinations, and unattended execution controls. |

Incremental and snapshot are per-run choices. Keeping mode out of JSON prevents a saved
project setting from overriding an explicit choice in the wizard or command.

Drop permission is also per run. Persistent configuration cannot enable destructive changes.

## Configuration resolution

1. Use the explicitly selected --config file, or discover the nearest .schemaalign.json
   upward from the working directory through the Git root. A worktree .git file counts as
   a root marker.
2. Load that single file. Parent configurations are not merged.
3. Explicit command schema locations override configured locations. The wizard reuses
   valid configured inputs and asks for missing or invalid ones.
4. Resolve schema paths in JSON against the configuration file's directory. Resolve paths
   entered in the wizard or CLI against the working directory.
5. Apply built-in defaults to omitted settings. Lists are not appended across configuration
   layers; imports and equivalent attribute spellings are normalized and deduplicated.

Unknown or duplicate keys, invalid JSON value types, empty values, and unsupported choices
produce actionable configuration errors. A missing explicitly selected file is an error;
there is no fallback to another file. Connection strings remain intact rather than being
split as file lists.

See the [configuration reference](configuration.md) for supported keys, defaults, and examples,
and the [migration guide](migration.md) for removed settings and flags.

## Review and application

The wizard validates inputs, asks whether the desired schema describes a sprint or the whole
system, and displays the effective inputs and comparison results. Synchronization then
requests removal permission only when needed, presents the change checklist, and generates
previews before the final action.

C# and SQL script workflows default to finishing the preview without applying. Live SQL
Server workflows offer script export, direct execution with confirmation, or cancellation.
Unattended execution uses --yes; combine it with --dry-run for a preview without schema changes.

Console summaries identify the comparison mode and write destination while replacing database
connection strings with a generic label. JSON and Markdown output remain machine-readable.

See [getting started](getting-started.md) for the guided flow and the [CLI reference](cli.md)
for automation, reports, and SQL export.

## Comparison scope and destination support

Incremental preserves tables and foreign keys omitted from the desired schema. Included
tables must still contain their complete desired column definitions. Snapshot reports
omitted tables, columns, and foreign keys for removal.

A reported difference is not a guarantee that a destination supports applying it. C# generation
conventions do not restyle existing classes, and CLI property rewrites do not implement every
reported removal. Review previews to see the actual operations.

See [comparison modes](comparison-modes.md) and [C# configuration](configuration.md) for the
behavior and limitations.
