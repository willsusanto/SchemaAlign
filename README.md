# SchemaAlign

Universal database and entity schema alignment tool for .NET. SchemaAlign parses schemas from Mermaid ER diagrams, C# EF Core entities, and SQL Server, calculates non-destructive incremental or snapshot diffs, and synchronizes target codebases with interactive Spectre.Console previews and safeguards.

## Features

- **Multi-Source Parsing**: Reads schemas from Mermaid ER diagrams (`.mmd`, `.mermaid`), C# entity classes/directories (`.cs`, including semicolon- or comma-separated multi-paths), SQL scripts (`.sql`), and SQL Server databases. Two-pass Mermaid parsing resolves foreign key relationships regardless of whether entities or relationships are declared first.
- **Diff Modes**:
  - `incremental` (default): Designed for sprint diagrams; compares changes while preserving existing unmentioned tables in your codebase.
  - `snapshot`: Treats target schema as the complete source of truth; marks unmentioned tables and columns for removal.
- **Spectre.Console Visual UI & Output Truncation**: Colorized diff summary tables, detailed property-level change trees, and syntax-highlighted unified diff previews, with automatic console truncation (capped at 15 tables and 3 file preview panels) to prevent terminal scrollback overflow.
- **Markdown Diff & Sync Reports**: Auto-exports comprehensive markdown reports to `.schemaalign/diff_report.md` or `.schemaalign/sync_report.md` when changes exceed console limits, or on demand via `-r` / `--report`.
- **Interactive Sync & Granular Checklist**: Multi-select prompt to toggle individual additions, modifications, and drop operations before applying changes.
- **Destructive Change Safeguards**: Protects against accidental table and column drops unless explicitly enabled with `--allow-drop`.
- **In-place Roslyn Entity Rewriter**: Updates existing C# entity classes while preserving custom methods, comments, and formatting.
- **Idempotent SQL Server Migration Applier**: Generates guarded T-SQL DDL migration scripts (`.sql`) and applies schema updates directly against live SQL Server databases.
- **Configuration & Conventions**: Supports `.schemaalign.json` configuration files with directory hierarchy traversal, configurable Foreign Key placement (`scalar` vs `navigation`), table prefix stripping (configured and automatic), custom base classes with auto-inherited property omission, custom class attributes, and extra using directives.

## CLI Usage

Run `SchemaAlign` with no arguments to start the interactive console wizard.

The wizard checks local schema files and C# entity directories immediately after entry and asks again if a path is missing or unsupported. Every entry in a multi-path C# input is checked. Relative paths use the working directory displayed by the wizard; surrounding quotes and whitespace are accepted. SQL Server connection strings bypass filesystem validation; database connectivity is checked when the schema is read.

```bash
dotnet run --project SchemaAlign
```

### Commands

#### `diff`
Compare the current base schema against the desired target schema non-destructively:

```bash
dotnet run --project SchemaAlign -- diff --current ./src/Entities --target ./docs/schema.mmd
```

**Options**:
- `-c`, `--current`: Path to current/base schema (`.cs`, entity directory or semicolon/comma-separated multi-paths, or database connection string; required if not specified in configuration).
- `-t`, `--target`: Path to desired/target schema (`.mmd`, `.cs`, etc.; required if not specified in configuration).
- `-m`, `--mode`: Diff mode (`incremental` [default] or `snapshot`).
- `-o`, `--output`: Output format (`console` [default], `json`, `markdown`).
- `-r`, `--report`: File path to write a detailed markdown report (aliases: `--report-file`; auto-exports to `.schemaalign/diff_report.md` when changed tables exceed 15).
- `--detailed`: Display detailed property-level change tree (types, nullability, lengths).
- `--table-prefix`: Known table prefix to strip when matching foreign key columns (can be specified multiple times). Aliases: `--table-prefixes`.
- `--config`: Path to `.schemaalign.json` configuration file.

#### `sync`
Synchronize the current base schema to match the desired target schema:

```bash
dotnet run --project SchemaAlign -- sync --current ./src/Entities --target ./docs/schema.mmd --interactive
```

**Options**:
- `-c`, `--current`: Path to current/base schema to be updated (`.cs`, entity directory or multi-paths, `.sql`, or database connection string; updates apply to primary path; required if not specified in configuration).
- `-t`, `--target`: Path to desired/target schema to align toward (required if not specified in configuration).
- `-o`, `--output-file`: Path to write the generated SQL Server migration script (e.g. `./migrations/migration.sql`) without applying directly to live database.
- `-r`, `--report`: File path to write a detailed markdown report (aliases: `--report-file`; auto-exports to `.schemaalign/sync_report.md` when changed tables exceed 15).
- `-m`, `--mode`: Diff mode (`incremental` [default] or `snapshot`).
- `-i`, `--interactive`: Run interactive checklist prompt to toggle individual changes (default: `true`).
- `--allow-drop`: Allow destructive drops (`DROP TABLE`, `DROP COLUMN`) during synchronization.
- `--no-drop`: Explicitly block destructive drops (default in automated mode).
- `--dry-run`: Generate and preview unified diffs without modifying files on disk or live databases.
- `-y`, `--yes`: Apply changes non-interactively without confirmation prompt.
- `--namespace`: Target C# namespace for generated entities (defaults to auto-detection from source files or `Entities`). Aliases: `--ns`.
- `--config`: Path to `.schemaalign.json` configuration file.
- `--base-class`: Base class for newly generated C# entity classes (e.g. `AuditEntity`).
- `--using`: Additional using namespace directives to add to generated entity files (can be specified multiple times).
- `--class-attribute`: Custom class-level attributes to emit on generated entity classes (can be specified multiple times). Aliases: `--class-attr`.
- `--foreign-key-placement`: Placement of the `[ForeignKey]` data annotation attribute (`scalar` or `navigation` [default]). Aliases: `--fk-placement`.
- `--table-prefix`: Table prefixes to strip when matching foreign key columns and generating navigation properties (can be specified multiple times). Aliases: `--table-prefixes`.

#### `inspect`
Inspect and display parsed tables, columns, and foreign keys from a schema source:

```bash
dotnet run --project SchemaAlign -- inspect --current ./docs/schema.mmd
```

**Options**:
- `-c`, `--current` *(required)*: Path to schema file or directory to inspect (supports semicolon/comma-separated multi-paths).
- `-o`, `--output`: Output format (`console` [default] or `json`).

## Configuration (`.schemaalign.json`)

SchemaAlign automatically discovers `.schemaalign.json` (or `schemaalign.json`) by searching upward from the current working directory to the repository root. You can also specify an explicit configuration file using the `--config` option on `diff` and `sync`.

### Example Configuration

```json
{
  "mode": "incremental",
  "allowDrop": false,
  "output": "console",
  "detailed": false,
  "tablePrefixes": [
    "tbl_",
    "px_"
  ],
  "csharp": {
    "namespace": "MyApp.Domain.Entities",
    "baseClass": "AuditEntity",
    "useFileScopedNamespaces": true,
    "useDataAnnotations": true,
    "foreignKeyPlacement": "scalar",
    "tablePrefixes": [
      "ms",
      "lt",
      "tr"
    ],
    "usings": [
      "MyApp.Domain.Common",
      "MyApp.Infrastructure.Attributes"
    ],
    "classAttributes": [
      "[DatabaseName(\"MainDb\")]"
    ],
    "omitInheritedColumns": [
      "CreatedBy",
      "CreatedAt",
      "UpdatedBy",
      "UpdatedAt"
    ]
  }
}
```

### Base Class Inheritance & Property Omission

When `baseClass` is configured (via `.schemaalign.json` or `--base-class`), SchemaAlign:
- Generates classes inheriting from the specified base class (`public class User : AuditEntity`).
- Analyzes existing source files to discover all properties declared across the base class inheritance hierarchy and automatically omits duplicate column declarations in newly generated entity classes.
- Automatically resolves and imports the namespace containing the base class if it resides in a different namespace.
- Supports explicit property omissions via `omitInheritedColumns`.

### Foreign Key Placement & Table Prefixes

- **Foreign Key Placement (`foreignKeyPlacement` / `--foreign-key-placement`)**:
  - `navigation` (default): Emits `[ForeignKey("ScalarPropId")]` on the navigation property.
  - `scalar`: Emits `[ForeignKey("NavigationProp")]` on the scalar foreign key property and omits `[ForeignKey]` from the navigation property.
- **Table Prefix Stripping (`tablePrefixes` / `--table-prefix`)**:
  - Strips configured prefixes (e.g. `tbl_`, `ms`, `lt`, `tr`) when matching foreign key candidate columns in Mermaid diagrams, generating navigation property names, and resolving foreign key constraints in SQL Server migrations.
  - Automatically detects 2-character PascalCase prefixes (such as `MsAuthor` or `LtBook`) during foreign key column resolution.

### SQL Server Safe Migrations & Constraint Guards

When generating or applying SQL Server DDL migrations:
- **Safe Column Drops**: Before issuing `ALTER TABLE ... DROP COLUMN`, SchemaAlign automatically cleans up any dependent foreign keys in `sys.foreign_key_columns` and default constraints in `sys.default_constraints` attached to the column to avoid SQL Server dependency check violations (`Msg 5074`).
- **Guarded Foreign Key Additions**: Generated foreign keys include metadata checks verifying that both the referencing column and the referenced primary key column exist before attempting constraint creation, preventing batch aborts (`Msg 1769`) and transaction rollbacks (`Msg 3902`).
- **Primary Key & Column Resolution**: Resolves actual primary key column names from parent tables across target or source schemas when generic `Id` is supplied, and resolves convention-based foreign key columns matching parent table prefixes.

### Console Output Truncation & Markdown Reports

When diffing or synchronizing large schemas, SchemaAlign caps terminal console output to prevent buffer overflow:
- **Diff Display**: Truncates console output at 15 changed tables across both summary tables and detailed change trees when a report has been saved. If automatic report export fails, all changed tables remain visible.
- **File Preview Panels**: When more than 3 files change and their full diffs have been saved in a report, displays a compact summary table with line change metrics. Without a saved preview report, every detailed file diff remains visible before confirmation.
- **Automatic Markdown Export**: When changed table counts exceed 15, a full Markdown report is automatically exported to `.schemaalign/diff_report.md` (for `diff`) or `.schemaalign/sync_report.md` (for `sync`).
- **Pre-Prompt Sync Inspection**: When a report is requested or automatically exported, interactive sync mode (`--interactive`) writes it before displaying the checklist prompt, allowing inspection of truncated changes before making selections.
- **Explicit Export**: Use `-r`, `--report`, or `--report-file <path>` to export the full Markdown report to a custom file path, including an up-to-date report when no changes are needed. Failure to write an explicitly requested report returns a nonzero exit code; sync stops before applying changes.
- **Output and Privacy**: Report titles replace database connection strings with a generic label. JSON and Markdown stdout contain only the requested format; report-write diagnostics go to stderr for these formats.
