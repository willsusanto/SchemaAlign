# SchemaAlign

Universal database and entity schema alignment tool for .NET. SchemaAlign parses schemas from Mermaid ER diagrams, C# EF Core entities, and SQL Server, calculates non-destructive incremental or snapshot diffs, and synchronizes target codebases with interactive Spectre.Console previews and safeguards.

## Features

- **Multi-Source Parsing**: Reads schemas from Mermaid ER diagrams (`.mmd`, `.mermaid`), C# entity classes/directories (`.cs`, including semicolon- or comma-separated multi-paths), SQL scripts (`.sql`), and SQL Server databases.
- **Diff Modes**:
  - `incremental` (default): Designed for sprint diagrams; compares changes while preserving existing unmentioned tables in your codebase.
  - `snapshot`: Treats target schema as the complete source of truth; marks unmentioned tables and columns for removal.
- **Spectre.Console Visual UI**: Colorized diff summary tables, detailed property-level change trees, and syntax-highlighted unified diff previews.
- **Interactive Sync & Granular Checklist**: Multi-select prompt to toggle individual additions, modifications, and drop operations before applying changes.
- **Destructive Change Safeguards**: Protects against accidental table and column drops unless explicitly enabled with `--allow-drop`.
- **In-place Roslyn Entity Rewriter**: Updates existing C# entity classes while preserving custom methods, comments, and formatting.
- **Idempotent SQL Server Migration Applier**: Generates guarded T-SQL DDL migration scripts (`.sql`) and applies schema updates directly against live SQL Server databases.
- **Excel Data Dictionary Export**: Exports Mermaid ER diagrams to styled Excel (`.xlsx`) data dictionaries with AID, database metadata, foreign key references, table styling, and table classification background highlighting.
- **Configuration & Conventions**: Supports `.schemaalign.json` configuration files with directory hierarchy traversal, custom base classes with auto-inherited property omission, custom class attributes, and extra using directives.

## CLI Usage

Run `SchemaAlign` with no arguments to start the interactive console wizard:

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
- `--detailed`: Display detailed property-level change tree (types, nullability, lengths).
- `--config`: Path to `.schemaalign.json` configuration file.

#### `sync`
Synchronize the current base schema to match the desired target schema:

```bash
dotnet run --project SchemaAlign -- sync --current ./src/Entities --target ./docs/schema.mmd --interactive
```

**Options**:
- `-c`, `--current`: Path to current/base schema to be updated (`.cs`, entity directory or multi-paths, `.sql`, or database connection string; updates apply to primary path; required if not specified in configuration).
- `-t`, `--target`: Path to desired/target schema to align toward (required if not specified in configuration).
- `-o`, `--output-file`: Optional path to export generated .sql migration script to file without applying directly to live database.
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

#### `inspect`
Inspect and display parsed tables, columns, and foreign keys from a schema source:

```bash
dotnet run --project SchemaAlign -- inspect --current ./docs/schema.mmd
```

**Options**:
- `-c`, `--current` *(required)*: Path to schema file or directory to inspect (supports semicolon/comma-separated multi-paths).
- `-o`, `--output`: Output format (`console` [default] or `json`).

#### `export`
Export Mermaid schema to an Excel Data Dictionary (`.xlsx`):

```bash
dotnet run --project SchemaAlign -- export --target ./docs/schema.mmd --output ./docs/dictionary.xlsx
```

**Options**:
- `-t`, `--target` *(required)*: Path to Mermaid schema file (`.mmd`, `.mermaid`).
- `-o`, `--output` *(required)*: Path to output Excel file (`.xlsx`).
- `-c`, `--config`: Optional path to `.schemaalign.json` configuration file.
- `--aid`: Application ID (AID) metadata value.
- `--ip`: IP / Domain / Azure Cosmos host metadata value.
- `--db`: SQL DB / Azure DB / Cosmos DB name metadata value.
- `--title`: System title header value.

Tables classified in Mermaid diagrams (via `class TableA newTbl` statements or `TableA:::newTbl` inline notation) matching configured highlight classes (`newTbl`, `updatedTbl` by default) receive a background row highlight (`#ffcccc` by default, configurable via `.schemaalign.json`).

## Configuration (`.schemaalign.json`)

SchemaAlign automatically discovers `.schemaalign.json` (or `schemaalign.json`) by searching upward from the current working directory to the repository root. You can also specify an explicit configuration file using the `--config` option on `diff` and `sync`.

### Example Configuration

```json
{
  "mode": "incremental",
  "allowDrop": false,
  "output": "console",
  "detailed": false,
  "csharp": {
    "namespace": "MyApp.Domain.Entities",
    "baseClass": "AuditEntity",
    "useFileScopedNamespaces": true,
    "useDataAnnotations": true,
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
  },
  "dictionary": {
    "aid": "1191",
    "ip": "db.corp.internal",
    "database": "MAIN_DB",
    "systemTitle": "Main System",
    "columnDefaults": {
      "stsrc": { "notes": "Status record data", "sample": "0.1" },
      "created_by": { "notes": "Record creator identifier", "sample": "USR-001" }
    }
  }
}
```

### Base Class Inheritance & Property Omission

When `baseClass` is configured (via `.schemaalign.json` or `--base-class`), SchemaAlign:
- Generates classes inheriting from the specified base class (`public class User : AuditEntity`).
- Analyzes existing source files to discover all properties declared across the base class inheritance hierarchy and automatically omits duplicate column declarations in newly generated entity classes.
- Automatically resolves and imports the namespace containing the base class if it resides in a different namespace.
- Supports explicit property omissions via `omitInheritedColumns`.
