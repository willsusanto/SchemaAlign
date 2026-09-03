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
