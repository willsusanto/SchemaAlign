# Commands and automation

[← SchemaAlign](../README.md#documentation)

Use the wizard for guided review. Commands are useful for a quick comparison, scripts, or continuous integration.

**Compare without changing either schema:**

```powershell
dotnet run --project SchemaAlign -- diff --current ./Masked.Entities --target ./masked-sprint.mmd
```

**Preview synchronization without prompts or schema changes:**

```powershell
dotnet run --project SchemaAlign -- sync --current ./Masked.Entities --target ./masked-sprint.mmd --dry-run --yes
```

If current and target are configured, omit those two options. Mode defaults to incremental; use `--mode` snapshot for a complete-system comparison.

## More examples: apply, inspect, export SQL, and save reports

Apply changes without interactive selection or confirmation. Destructive drops remain excluded unless `--allow-drop` is supplied:

```powershell
dotnet run --project SchemaAlign -- sync --yes
```

Inspect a single schema as JSON:

```powershell
dotnet run --project SchemaAlign -- inspect --current ./masked-sprint.mmd --output json
```

Save a complete Markdown comparison report:

```powershell
dotnet run --project SchemaAlign -- diff --mode snapshot --report ./reports/masked-diff.md
```

Export a SQL migration instead of applying directly to a database:

```powershell
dotnet run --project SchemaAlign -- sync --current ./masked-current.sql --target ./masked-sprint.mmd --output-file ./migrations/masked-update.sql --yes
```

`--yes` disables selection and confirmation. `--dry-run` prevents schema changes, but requested or automatic reports can still be written.

The most useful options are schema locations, comparison mode, and preview/report controls. C# conventions belong in JSON.

## All supported options and defaults

**Shared by diff and sync**

| Option | Short form | Usage |
| --- | --- | --- |
| `--current` | -c | Existing schema. Required unless provided by configuration. Sync updates this destination. |
| `--target` | -t | Desired schema. Required unless provided by configuration. |
| `--mode` | -m | incremental (default) or snapshot. Chosen per run. |
| `--config` | — | Explicit JSON configuration path instead of automatic discovery. |
| `--report` | -r | Write a complete Markdown report to a file. |

**Additional diff options**

| Option | Short form | Usage |
| --- | --- | --- |
| `--output` | -o | console (default), json, markdown, or md. |
| `--detailed` | — | Show property-level changes in console output. Default false. |

**Additional sync options**

| Option | Short form | Usage |
| --- | --- | --- |
| `--output-file` | -o | Write the SQL migration to a file. Applies to SQL destinations. |
| `--allow-drop` | — | Permit destructive drops for this run. Default false. |
| `--dry-run` | — | Preview without applying schema changes. Default false. |
| `--yes` | -y | Skip the checklist and confirmation for unattended execution. Default false. |

**Inspect options**

| Option | Short form | Usage |
| --- | --- | --- |
| `--current` | -c | Required schema location. Accepts combined C# paths. |
| `--output` | -o | console (default) or json. |

All commands support `--help` (-h, -?). For example:

```powershell
dotnet run --project SchemaAlign -- sync --help
```

Note that -o selects an **output format** for diff/inspect and a **SQL output file** for sync. Long names make that distinction explicit.



## Reports and large comparisons

Console output stays manageable while full reports preserve the detail:

- More than 15 changed tables triggers an automatic Markdown report in .schemaalign/diff_report.md or .schemaalign/sync_report.md.
- Once a report is saved, console table summaries and detailed trees are capped at 15 changed tables.
- More than three file previews use a compact summary only when full previews have been saved in a report.
- If an automatic report cannot be saved, detailed changes remain visible instead of being hidden by truncation.

Use `--report` to choose a path even for small comparisons. Explicit reports are refreshed when no changes are needed. A failed explicit report write returns a nonzero exit code; synchronization stops before application.

Interactive sync saves requested/automatic comparison reports before the checklist, so you can inspect them before selecting changes. Reports are updated with file previews afterward.

JSON and Markdown diff output contain only the requested format; report-write diagnostics use stderr. Console setup summaries and report titles replace database connection strings with a generic label.



## SQL Server migration behavior

SQL migration generation includes guards for common dependency and repeat-execution cases:

- Before dropping a column, dependent foreign keys and default constraints are removed.
- Foreign-key creation checks that referenced and dependent columns exist.
- Primary-key resolution uses available schema information instead of assuming every key is named Id.
- Configured prefixes help resolve convention-based foreign-key names.

The wizard lets you export a script or explicitly confirm direct live execution. Selecting snapshot defines which removals are reported; permission and selection still control which are included in synchronization.
