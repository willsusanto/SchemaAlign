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
- **Excel Data Dictionary Export**: Exports Mermaid ER diagrams to styled Excel (`.xlsx`) data dictionaries with AID, database metadata, foreign key references, table styling, and table classification background highlighting.

## CLI Usage

Run `SchemaAlign` with no arguments to start the interactive console wizard:

```bash
dotnet run --project SchemaAlign
```

### Commands

#### `diff`
Compare the current base schema against the desired target schema non-destructively:

```bash
dotnet run --project SchemaAlign -- diff --source ./src/Entities --target ./docs/schema.mmd
```

**Options**:
- `-s`, `--source` *(required)*: Path to current/base schema (`.cs`, entity directory or semicolon/comma-separated multi-paths, or database). Aliases: `--from`, `--current`, `--base`.
- `-t`, `--target` *(required)*: Path to desired/target schema (`.mmd`, `.cs`, etc.). Aliases: `--to`, `--desired`.
- `-m`, `--mode`: Diff mode (`incremental` [default] or `snapshot`).
- `-o`, `--output`: Output format (`console` [default], `json`, `markdown`).
- `--detailed`: Display detailed property-level change tree (types, nullability, lengths).

#### `sync`
Synchronize the current base schema to match the desired target schema:

```bash
dotnet run --project SchemaAlign -- sync --source ./src/Entities --target ./docs/schema.mmd --interactive
```

**Options**:
- `-s`, `--source` *(required)*: Path to current/base schema to be updated (supports semicolon/comma-separated multi-paths; updates apply to primary path). Aliases: `--from`, `--current`, `--base`.
- `-t`, `--target` *(required)*: Path to desired/target schema to align toward. Aliases: `--to`, `--desired`.
- `-m`, `--mode`: Diff mode (`incremental` [default] or `snapshot`).
- `-i`, `--interactive`: Run interactive checklist prompt to toggle individual changes (default: `true`).
- `--allow-drop`: Allow destructive drops (`DROP TABLE`, `DROP COLUMN`) during synchronization.
- `--no-drop`: Explicitly block destructive drops (default in automated mode).
- `--dry-run`: Generate and preview unified diffs without modifying files on disk.
- `-y`, `--yes`: Apply changes non-interactively without confirmation prompt.
- `--namespace`: Target C# namespace for generated entities (defaults to auto-detection from source files or `Entities`). Aliases: `--ns`.

#### `inspect`
Inspect and display parsed tables, columns, and foreign keys from a schema source:

```bash
dotnet run --project SchemaAlign -- inspect --source ./docs/schema.mmd
```

**Options**:
- `-s`, `--source` *(required)*: Path to schema file or directory to inspect (supports semicolon/comma-separated multi-paths). Aliases: `--from`.
- `-o`, `--output`: Output format (`console` [default] or `json`).

#### `export`
Export Mermaid schema to an Excel Data Dictionary (`.xlsx`):

```bash
dotnet run --project SchemaAlign -- export --source ./docs/schema.mmd --output ./docs/dictionary.xlsx
```

**Options**:
- `-s`, `--source` *(required)*: Path to Mermaid schema file (`.mmd`, `.mermaid`). Aliases: `--from`.
- `-o`, `--output` *(required)*: Path to output Excel file (`.xlsx`).
- `-c`, `--config`: Optional path to `schemaalign.json` configuration file. Aliases: `-c`.
- `--aid`: Application ID (AID) metadata value.
- `--ip`: IP / Domain / Azure Cosmos host metadata value.
- `--db`: SQL DB / Azure DB / Cosmos DB name metadata value.
- `--title`: System title header value.

Tables classified in Mermaid diagrams (via `class TableA newTbl` statements or `TableA:::newTbl` inline notation) matching configured highlight classes (`newTbl`, `updatedTbl` by default) receive a background row highlight (`#ffcccc` by default, configurable via `schemaalign.json`).
