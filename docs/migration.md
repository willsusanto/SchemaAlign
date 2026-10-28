# Migrating configuration and commands

[← SchemaAlign](../README.md#documentation)

Older configuration files and flags need a small migration. Removed settings now produce errors rather than being silently ignored.

## Configuration and option migration guide

| Previous configuration | Use instead |
| --- | --- |
| `source` | current |
| `mode` | Wizard scope selection or `--mode` incremental/snapshot |
| `allowDrop` | Contextual wizard confirmation or `--allow-drop` |
| `output` | diff/inspect `--output` |
| `detailed` | diff `--detailed`; wizard comparisons already include details |
| `csharp.tablePrefixes` | Root tablePrefixes |
| $schema or other unrecognized metadata | Remove it; only the documented keys are accepted. |

Rename an automatically discovered schemaalign.json to **.schemaalign.json**. An explicitly selected `--config` file can still have a custom name.

| Previous CLI option | Use instead |
| --- | --- |
| `--namespace` / `--ns` | `csharp.namespace` in JSON |
| `--base-class` | `csharp.baseClass` in JSON |
| `--using` | `csharp.usings` in JSON |
| `--class-attribute` / `--class-attr` | `csharp.classAttributes` in JSON |
| `--foreign-key-placement` / `--fk-placement` | `csharp.foreignKeyPlacement` in JSON |
| `--table-prefix` / `--table-prefixes` | Root tablePrefixes in JSON |
| `--interactive` / -i | Default guided sync; `--yes` selects unattended execution |
| `--no-drop` | Omit `--allow-drop`; persistent configuration cannot enable drops |
| `--report-file` | `--report` or -r |

All eight C# settings remain supported in JSON. Their CLI duplicates were removed.

For the configuration resolution rules and workflow design, see [configuration workflow](configuration-workflow.md).
