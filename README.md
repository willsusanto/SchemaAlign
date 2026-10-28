# SchemaAlign

**Review schema changes before they reach your code or database.**

SchemaAlign is a .NET tool for comparing and aligning Mermaid ER diagrams, C# entities, SQL scripts, and SQL Server schemas. Start with the interactive wizard to see differences, select changes, and preview the result before applying.

## Why SchemaAlign?

- **Sprint-sized diagrams:** update the tables relevant to a sprint while preserving the rest of your system.
- **Review before applying:** inspect schema differences and generated C# or SQL changes.
- **Project conventions:** reuse C# namespaces, base classes, imports, and mapping attributes.
- **Repeatable automation:** compare schemas, export reports, and synchronize through a small CLI.

## Quick start

With the **.NET 10 SDK** installed, run this from the cloned repository's root:

```powershell
dotnet run --project SchemaAlign
```

Choose **compare**, **synchronize**, or **inspect**. The wizard reuses configured inputs or asks for their locations, then guides you through the relevant review steps.

**Current** is the existing schema that synchronization updates. **Target** describes the desired schema.

[Follow the getting-started guide →](docs/getting-started.md)

## Working across sprints

Choose **incremental** when your desired schema includes only this sprint's tables; tables omitted from it are preserved. Choose **snapshot** when it describes the complete system; omitted tables are reported for removal.

Included tables must contain their **complete desired column definitions** in both modes. Select the mode in the wizard or with `--mode`, rather than in JSON.

[Understand comparison modes →](docs/comparison-modes.md)

## Documentation

| I want to… | Read |
| --- | --- |
| Run my first comparison and understand the inputs | [Getting started](docs/getting-started.md) |
| Compare a sprint diagram or a complete schema | [Comparison modes](docs/comparison-modes.md) |
| Save project locations and C# conventions | [Configuration reference](docs/configuration.md) |
| Include shared base classes or choose one database's entities | [C# source paths](docs/csharp-inputs.md) |
| Automate commands, export SQL, or save reports | [CLI reference and recipes](docs/cli.md) |
| Update an older configuration or command | [Migration guide](docs/migration.md) |

## Development

```powershell
dotnet test SchemaAlign.Tests/SchemaAlign.Tests.csproj
dotnet build SchemaAlign.slnx
```

See [configuration workflow](docs/configuration-workflow.md) for the configuration and wizard changes.
